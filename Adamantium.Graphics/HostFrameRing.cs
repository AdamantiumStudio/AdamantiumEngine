using System;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using VulkanBuffer = Adamantium.Vulkan.Core.Buffer;

namespace Adamantium.Graphics;

/// <summary>A ring of frames in <see cref="SharedFrameMemory"/>, each slot imported into a device as a buffer its frames
/// are copied into, with the descriptors a consumer finds them by.</summary>
internal sealed unsafe class HostFrameRing : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly VulkanBuffer[] _buffers;
    private readonly DeviceMemory[] _memories;

    public HostFrameRing(IGraphicsDevice device, uint width, uint height, SurfaceFormat format, uint slotCount)
    {
        _device = device;
        Width = width;
        Height = height;
        Memory = SharedFrameMemory.Create((ulong)width * height * (ulong)format.SizeInBytes, slotCount,
            Math.Max(device.Adapter.HostPointerAlignment, 4096));
        _buffers = new VulkanBuffer[slotCount];
        _memories = new DeviceMemory[slotCount];
        Descriptors = new SharedSurfaceDescriptor[slotCount];
        try
        {
            Timeline = device.LogicalDevice.CreateSemaphore(new SemaphoreCreateInfo
            {
                PNext = new SemaphoreTypeCreateInfo { SemaphoreType = SemaphoreType.Timeline, InitialValue = 0 }
            });
            for (uint slot = 0; slot < slotCount; slot++)
            {
                Import(slot);
                Descriptors[slot] = new SharedSurfaceDescriptor
                {
                    HandleType = SharedHandleType.HostMemory,
                    MemoryPath = Memory.Path,
                    MemoryOffset = Memory.SlotOffset(slot),
                    CounterOffset = Memory.CounterOffset(slot),
                    AllocationSize = Memory.SlotSize,
                    Width = width,
                    Height = height,
                    Format = format
                };
            }
        }
        catch
        {
            Dispose();
            throw;
        }

        Descriptors[0].BufferCount = slotCount;
        Descriptors[0].Others = Descriptors[1..];
    }

    public IGraphicsDevice Device => _device;

    public uint Width { get; }

    public uint Height { get; }

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

    /// <summary>One per slot; the first names the others.</summary>
    public SharedSurfaceDescriptor[] Descriptors { get; }

    /// <summary>Whether <paramref name="device"/> imports the memory of the process as its own.</summary>
    public static bool CanImport(IGraphicsDevice device) =>
        device.Adapter.HostPointerAlignment != 0
        && device.MainDevice.IsExtensionEnabled(Constants.VK_EXT_EXTERNAL_MEMORY_HOST_EXTENSION_NAME);

    /// <summary>Records into the device's current command buffer a copy of <paramref name="frame"/> into slot
    /// <paramref name="slot"/>; after EndDraw, before Submit.</summary>
    public void RecordCopy(ITexture frame, uint slot)
    {
        var commandBuffer = _device.CurrentCommandBuffer;
        _device.InsertImageMemoryBarrier(commandBuffer, frame,
            AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.TransferReadBit,
            ImageLayout.ColorAttachmentOptimal, ImageLayout.TransferSrcOptimal,
            PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.AllTransferBit);
        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = Width,
            BufferImageHeight = Height,
            ImageSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlagBits.ColorBit, LayerCount = 1 },
            ImageOffset = new Offset3D(),
            ImageExtent = new Extent3D
            {
                Width = Math.Min(frame.Width, Width),
                Height = Math.Min(frame.Height, Height),
                Depth = 1
            }
        };
        commandBuffer.CopyImageToBuffer(frame.GetImage(), ImageLayout.TransferSrcOptimal, _buffers[slot], 1, region);
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
        for (var slot = 0; slot < _buffers.Length; slot++)
        {
            if (_buffers[slot] != null)
            {
                _device.Destroy(_buffers[slot]);
            }

            if (_memories[slot] != null)
            {
                _device.Destroy(_memories[slot]);
            }
        }

        if (Timeline != null)
        {
            _device.Destroy(Timeline);
        }

        Memory.Dispose();
    }

    private void Import(uint slot)
    {
        var pointer = (nuint)Memory.At(Memory.SlotOffset(slot));
        var buffer = _device.LogicalDevice.CreateBuffer(new BufferCreateInfo
        {
            Size = Memory.SlotSize,
            Usage = BufferUsageFlagBits.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            PNext = new ExternalMemoryBufferCreateInfo { HandleTypes = ExternalMemoryHandleTypeFlagBits.HostAllocationBitExt }
        });
        _buffers[slot] = buffer;
        var result = _device.LogicalDevice.GetMemoryHostPointerPropertiesEXT(
            ExternalMemoryHandleTypeFlagBits.HostAllocationBitExt, pointer, out var hostProperties);
        if (result != Result.Success)
        {
            throw new NotSupportedException($"The device takes no memory of the process: {result}.");
        }

        var requirements = _device.LogicalDevice.GetBufferMemoryRequirements(buffer);
        var typeBits = requirements.MemoryTypeBits & hostProperties.MemoryTypeBits;
        if (!_device.Adapter.TryFindMemoryIndex(typeBits, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent,
                out var typeIndex)
            && !_device.Adapter.TryFindMemoryIndex(typeBits, MemoryPropertyFlags.HostVisible, out typeIndex)
            && !_device.Adapter.TryFindMemoryIndex(typeBits, 0, out typeIndex))
        {
            throw new NotSupportedException($"No memory type takes the process's memory (0x{typeBits:X}).");
        }

        result = _device.MainDevice.AllocateMemory(new MemoryAllocateInfo
        {
            AllocationSize = Memory.SlotSize,
            MemoryTypeIndex = typeIndex,
            PNext = new ImportMemoryHostPointerInfoEXT
            {
                HandleType = ExternalMemoryHandleTypeFlagBits.HostAllocationBitExt,
                PHostPointer = pointer
            }
        }, out var memory);
        if (result != Result.Success)
        {
            throw new NotSupportedException($"The process's memory could not be imported: {result}.");
        }

        _memories[slot] = memory;
        result = _device.LogicalDevice.BindBufferMemory(buffer, memory, 0);
        if (result != Result.Success)
        {
            throw new NotSupportedException($"The imported memory could not be bound: {result}.");
        }
    }
}
