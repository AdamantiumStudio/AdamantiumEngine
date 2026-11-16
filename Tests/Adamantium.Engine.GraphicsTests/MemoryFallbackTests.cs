using System;
using System.Collections.Generic;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

[TestFixture]
public class MemoryFallbackTests
{
    private const ulong Chunk = 4UL * 1024 * 1024;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    /// <summary>The device-local host-visible window is shared by every process on the GPU, so it can be full before this
    /// one asks: a buffer that wants it still gets memory the CPU can write, from system memory.</summary>
    [Test]
    public void UploadBuffer_WhenTheWindowIsFull_TakesSystemMemory()
    {
        var device = (GraphicsDevice)GpuFixture.CreateRenderDevice();
        var window = FindWindowType(device, out var windowHeapSize);
        if (window == null || windowHeapSize > 1024UL * 1024 * 1024)
        {
            Assert.Ignore("this GPU has no small host-visible device-local window to fill (Resizable BAR or none)");
        }

        var filler = new List<DeviceMemory>();
        try
        {
            var info = new MemoryAllocateInfo { AllocationSize = Chunk, MemoryTypeIndex = window.Value };
            var full = false;
            while (!full && (ulong)filler.Count * Chunk <= windowHeapSize)
            {
                full = device.LogicalDevice.AllocateMemory(info, null, out var memory) != Result.Success;
                if (!full)
                {
                    filler.Add(memory);
                }
            }

            // Some drivers never refuse it: amdgpu on Linux places what does not fit beside the window, so the window
            // cannot be filled and there is no fallback to see.
            if (!full)
            {
                Assert.Ignore("the driver gives memory of the window's type past its size, so it cannot be filled");
            }

            var data = new uint[2 * Chunk / sizeof(uint)];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (uint)i * 2654435761u;
            }

            using var buffer = Adamantium.Graphics.Buffer.New(device, 2 * Chunk, BufferUsageFlags.StorageBuffer,
                BufferMemoryUsage.UploadFromCpuToGpu);
            buffer.SetData(data);

            Assert.That(buffer.GetData<uint>(), Is.EqualTo(data));
        }
        finally
        {
            foreach (var memory in filler)
            {
                device.Destroy(memory);
            }
        }
    }

    /// <summary>A block made for one allocation larger than a shared block goes back to the driver once that allocation
    /// is freed and the frames in flight are done with it; a shared block stays for the next.</summary>
    [Test]
    public void ABlockMadeForOneLargeAllocation_GoesBackToTheDriver_ASharedOneStays()
    {
        var device = (GraphicsDevice)GpuFixture.CreateRenderDevice();
        using var allocator = new DeviceMemoryAllocator(device);
        var type = FindSystemType(device);
        Assume.That(type, Is.Not.Null, "the GPU has no host-visible memory");

        var small = allocator.Allocate(Chunk, 256, type.Value, 1u << (int)type.Value, false);
        var withShared = allocator.BytesInBlocks;
        allocator.Free(small);
        var afterShared = allocator.BytesInBlocks;
        var large = allocator.Allocate(80 * Chunk, 256, type.Value, 1u << (int)type.Value, false);
        var withLarge = allocator.BytesInBlocks;
        allocator.Free(large);
        var whileFramesMayReadIt = allocator.BytesInBlocks;
        GpuFixture.Main.OnFrameStarted();

        Assert.Multiple(() =>
        {
            Assert.That(afterShared, Is.EqualTo(withShared), "the shared block stays");
            Assert.That(withLarge - withShared, Is.GreaterThanOrEqualTo(80 * Chunk));
            Assert.That(whileFramesMayReadIt, Is.EqualTo(withLarge), "kept until the frames in flight are done");
            Assert.That(allocator.BytesInBlocks, Is.EqualTo(withShared), "the large one's block went back");
        });
    }

    /// <summary>Shared blocks left empty go back to the driver once the frames in flight are done with them, but one,
    /// kept for the next allocation: a peak would otherwise hold its memory for the life of the device.</summary>
    [Test]
    public void SharedBlocksLeftEmpty_GoBackToTheDriver_ButOne()
    {
        var device = (GraphicsDevice)GpuFixture.CreateRenderDevice();
        using var allocator = new DeviceMemoryAllocator(device);
        var type = FindSystemType(device);
        Assume.That(type, Is.Not.Null, "the GPU has no host-visible memory");

        var before = allocator.BytesInBlocks;
        var allocations = FillBlocks(allocator, type.Value, 3, out var oneBlock);
        foreach (var allocation in allocations)
        {
            allocator.Free(allocation);
        }

        GpuFixture.Main.OnFrameStarted();

        Assert.That(allocator.BytesInBlocks - before, Is.EqualTo(oneBlock), "one empty block of the three stays");
    }

    /// <summary>A shared block left empty and waiting to go back is taken back by an allocation that would otherwise
    /// make a new one, and then stays: a load swinging by a block does not allocate one each time.</summary>
    [Test]
    public void ASharedBlockWaitingToGoBack_IsTakenBackByTheNextThatNeedsOne()
    {
        var device = (GraphicsDevice)GpuFixture.CreateRenderDevice();
        using var allocator = new DeviceMemoryAllocator(device);
        var type = FindSystemType(device);
        Assume.That(type, Is.Not.Null, "the GPU has no host-visible memory");

        var before = allocator.BytesInBlocks;
        var allocations = FillBlocks(allocator, type.Value, 2, out var oneBlock);
        foreach (var allocation in allocations)
        {
            allocator.Free(allocation);
        }

        var again = Allocate(allocator, type.Value, allocations.Count);
        var whileRetiring = allocator.BytesInBlocks - before;
        GpuFixture.Main.OnFrameStarted();

        Assert.Multiple(() =>
        {
            Assert.That(whileRetiring, Is.EqualTo(2 * oneBlock), "no third block made");
            Assert.That(allocator.BytesInBlocks - before, Is.EqualTo(2 * oneBlock), "the block taken back stays");
        });

        foreach (var allocation in again)
        {
            allocator.Free(allocation);
        }
    }

    // Allocations of a shared block's smallest size until `blocks` blocks hold them; oneBlock is a block's size.
    private static List<DeviceMemoryAllocator.MemoryAllocation> FillBlocks(DeviceMemoryAllocator allocator, uint type,
        int blocks, out ulong oneBlock)
    {
        var before = allocator.BytesInBlocks;
        var allocations = new List<DeviceMemoryAllocator.MemoryAllocation>
        {
            allocator.Allocate(Chunk, 256, type, 1u << (int)type, false),
        };
        oneBlock = Math.Max(allocator.BytesInBlocks - before, Chunk);
        allocations.AddRange(Allocate(allocator, type, (int)(oneBlock / Chunk) * blocks - 1));
        return allocations;
    }

    private static List<DeviceMemoryAllocator.MemoryAllocation> Allocate(DeviceMemoryAllocator allocator, uint type,
        int count)
    {
        var allocations = new List<DeviceMemoryAllocator.MemoryAllocation>();
        while (allocations.Count < count)
        {
            allocations.Add(allocator.Allocate(Chunk, 256, type, 1u << (int)type, false));
        }

        return allocations;
    }

    private static uint? FindSystemType(GraphicsDevice device)
    {
        var properties = device.Adapter.Adapter.GetPhysicalDeviceMemoryProperties();
        for (var t = 0; t < properties.MemoryTypeCount; t++)
        {
            var flags = (MemoryPropertyFlags)properties.MemoryTypes.Span[t].PropertyFlags;
            if (flags.HasFlag(MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent))
            {
                return (uint)t;
            }
        }

        return null;
    }

    private static uint? FindWindowType(GraphicsDevice device, out ulong heapSize)
    {
        var properties = device.Adapter.Adapter.GetPhysicalDeviceMemoryProperties();
        var wanted = MemoryPropertyFlags.DeviceLocal | MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent;
        for (var t = 0; t < properties.MemoryTypeCount; t++)
        {
            var type = properties.MemoryTypes.Span[t];
            if (((MemoryPropertyFlags)type.PropertyFlags).HasFlag(wanted))
            {
                heapSize = properties.MemoryHeaps.Span[(int)type.HeapIndex].Size;
                return (uint)t;
            }
        }

        heapSize = 0;
        return null;
    }
}
