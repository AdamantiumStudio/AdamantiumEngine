using System;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

/// <summary>A ring of frames in <see cref="SharedFrameMemory"/>, each slot imported into a device as a buffer its frames
/// are copied into. A slot holds a frame of any size up to its own, and says the size it holds; the consumer finds the
/// frames of one size by <see cref="Describe"/>.</summary>
internal sealed unsafe class HostFrameRing : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly SurfaceFormat _format;
    private readonly HostSlotBuffer[] _slots;

    public HostFrameRing(IGraphicsDevice device, ulong slotCapacity, SurfaceFormat format, uint slotCount)
    {
        _device = device;
        _format = format;
        Memory = SharedFrameMemory.Create(slotCapacity, slotCount, Math.Max(device.Adapter.HostPointerAlignment, 4096));
        _slots = new HostSlotBuffer[slotCount];
        try
        {
            Timeline = device.LogicalDevice.CreateSemaphore(new SemaphoreCreateInfo
            {
                PNext = new SemaphoreTypeCreateInfo { SemaphoreType = SemaphoreType.Timeline, InitialValue = 0 }
            });
            for (uint slot = 0; slot < slotCount; slot++)
            {
                _slots[slot] = HostSlotBuffer.Import(device, (nuint)Memory.At(Memory.SlotOffset(slot)), Memory.SlotSize,
                    BufferUsageFlagBits.TransferDstBit);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IGraphicsDevice Device => _device;

    public SharedFrameMemory Memory { get; }

    /// <summary>Signaled by the device as each copy into a slot is done, with the value it was given.</summary>
    public Semaphore Timeline { get; }

    /// <summary>The last value <see cref="Timeline"/> reached: the copies given it and before are done.</summary>
    public ulong DoneValue
    {
        get
        {
            _device.LogicalDevice.GetSemaphoreCounterValue(Timeline, out var value);
            return value;
        }
    }


    /// <summary>The bytes of a <paramref name="width"/> by <paramref name="height"/> frame of
    /// <paramref name="format"/>.</summary>
    public static ulong FrameBytes(uint width, uint height, SurfaceFormat format) =>
        (ulong)width * height * (ulong)format.SizeInBytes;

    /// <summary>Whether a <paramref name="width"/> by <paramref name="height"/> frame fits in a slot.</summary>
    public bool Fits(uint width, uint height) => FrameBytes(width, height, _format) <= Memory.SlotSize;

    /// <summary>The descriptors the consumer takes the ring's frames of this size by, one per slot; the first names the
    /// others.</summary>
    public SharedSurfaceDescriptor[] Describe(uint width, uint height)
    {
        var descriptors = new SharedSurfaceDescriptor[_slots.Length];
        for (uint slot = 0; slot < _slots.Length; slot++)
        {
            descriptors[slot] = new SharedSurfaceDescriptor
            {
                HandleType = SharedHandleType.HostMemory,
                MemoryPath = Memory.Path,
                MemoryOffset = Memory.SlotOffset(slot),
                CounterOffset = Memory.CounterOffset(slot),
                AllocationSize = Memory.SlotSize,
                Width = width,
                Height = height,
                Format = _format
            };
        }

        descriptors[0].BufferCount = (uint)_slots.Length;
        descriptors[0].Others = descriptors[1..];
        return descriptors;
    }

    /// <summary>Says slot <paramref name="slot"/> holds frame <paramref name="frame"/> of this size, finished: the size
    /// first, so the consumer reads it with the frame.</summary>
    public void Finish(uint slot, ulong frame, uint width, uint height)
    {
        Memory.WriteFrameSize(Memory.CounterOffset(slot), width, height);
        Memory.WriteCounter(Memory.CounterOffset(slot), frame);
    }

    /// <summary>Records into the device's current command buffer a copy of the <paramref name="width"/> by
    /// <paramref name="height"/> <paramref name="frame"/> into slot <paramref name="slot"/>; after EndDraw, before
    /// Submit.</summary>
    public void RecordCopy(ITexture frame, uint slot, uint width, uint height)
    {
        var commandBuffer = _device.CurrentCommandBuffer;
        _device.InsertImageMemoryBarrier(commandBuffer, frame,
            AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.TransferReadBit,
            ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal,
            PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.AllTransferBit);
        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = width,
            BufferImageHeight = height,
            ImageSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlagBits.ColorBit, LayerCount = 1 },
            ImageOffset = new Offset3D(),
            ImageExtent = new Extent3D
            {
                Width = Math.Min(frame.Width, width),
                Height = Math.Min(frame.Height, height),
                Depth = 1
            }
        };
        commandBuffer.CopyImageToBuffer(frame.GetImage(), ImageLayout.TransferSrcOptimal, _slots[slot].Buffer, 1, region);
        commandBuffer.PipelineBarrier2(new DependencyInfo
        {
            PMemoryBarriers = new[]
            {
                new MemoryBarrier2
                {
                    SrcStageMask = PipelineStageFlagBits2.AllTransferBit,
                    SrcAccessMask = AccessFlagBits2.TransferWriteBit,
                    DstStageMask = PipelineStageFlagBits2.HostBit,
                    DstAccessMask = AccessFlagBits2.HostReadBit
                }
            },
            MemoryBarrierCount = 1
        });
        _device.InsertImageMemoryBarrier(commandBuffer, frame,
            AccessFlagBits2.TransferReadBit, AccessFlagBits2.ColorAttachmentWriteBit,
            ImageLayout.TransferSrcOptimal, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.ColorAttachmentOutputBit);
    }

    public void Dispose()
    {
        foreach (var slot in _slots)
        {
            slot?.Dispose();
        }

        if (Timeline != null)
        {
            _device.Destroy(Timeline);
        }

        Memory.Dispose();
    }
}
