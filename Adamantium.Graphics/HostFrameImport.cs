using System;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;
using VulkanBuffer = Adamantium.Vulkan.Core.Buffer;

namespace Adamantium.Graphics;

/// <summary>The consumer's side of a <see cref="SharedHandleType.HostMemory"/> ring on a device: copies the newest frame
/// into a surface on the GPU - from its slot imported as a buffer where the device takes memory of the process, else
/// through a staging buffer - and gives the slot back once the copy is done.</summary>
internal sealed class HostFrameImport : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly HostFrameReader _reader;
    private readonly HostSlotBuffer[] _slots;
    private readonly Buffer _staging;
    private readonly Semaphore _copies;
    private ulong _copyValue;
    private ulong _copiedFrame;
    private ulong _givenBack;

    public HostFrameImport(IGraphicsDevice device, SharedSurfaceDescriptor descriptor, bool importSlots = true)
    {
        _device = device;
        _reader = new HostFrameReader(descriptor);
        try
        {
            _copies = device.LogicalDevice.CreateSemaphore(new SemaphoreCreateInfo
            {
                PNext = new SemaphoreTypeCreateInfo { SemaphoreType = SemaphoreType.Timeline, InitialValue = 0 }
            });
            _slots = importSlots ? TryImportSlots() : null;
            if (_slots == null)
            {
                _staging = Buffer.New(device, (ulong)_reader.Pixels(0).Length, BufferUsageFlags.TransferSrc,
                    MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Whether the frames are copied from the slots themselves, not through a staging buffer.</summary>
    public bool ImportsSlots => _slots != null;

    /// <summary>Copies the newest frame finished since the last into <paramref name="target"/>, submitted on its own on
    /// <paramref name="device"/> ahead of its frame; gives the frame, or 0 when none is new or the last copy still runs.
    /// </summary>
    public ulong LatchInto(IGraphicsDevice device, ITexture target)
    {
        if (CopiesDone() < _copyValue)
        {
            return 0;
        }

        GiveBackCopied();
        if (!_reader.TryTakeNewest(out var slot, out var frame))
        {
            return 0;
        }

        VulkanBuffer source;
        if (_slots != null)
        {
            source = _slots[slot].Buffer;
        }
        else
        {
            _staging.SetData(_reader.Pixels(slot));
            source = _staging;
            _reader.GiveBack(frame);
            _givenBack = frame;
        }

        var copy = _copyValue + 1;
        var result = device.SubmitAside(commandBuffer => RecordCopy(device, commandBuffer, source, target),
            null, PipelineStageFlagBits2.None, 0, [(_copies, copy)]);
        if (device.MainDevice.Observe(result) != Result.Success)
        {
            return 0;
        }

        _copyValue = copy;
        _copiedFrame = frame;
        return frame;
    }

    public void Dispose()
    {
        if (_copies != null)
        {
            WaitForCopies();
            _device.Destroy(_copies);
        }

        if (_slots != null)
        {
            foreach (var slot in _slots)
            {
                slot?.Dispose();
            }
        }

        _staging?.Dispose();
        _reader.Dispose();
    }

    private HostSlotBuffer[] TryImportSlots()
    {
        if (!HostFrameRing.CanImport(_device) || !IsAligned())
        {
            return null;
        }

        var slots = new HostSlotBuffer[_reader.SlotCount];
        try
        {
            for (var slot = 0; slot < slots.Length; slot++)
            {
                slots[slot] = HostSlotBuffer.Import(_device, _reader.SlotAddress(slot), _reader.SlotSize,
                    BufferUsageFlagBits.TransferSrcBit);
            }
        }
        catch (Exception exception)
        {
            foreach (var slot in slots)
            {
                slot?.Dispose();
            }

            if (exception is NotSupportedException)
            {
                return null;
            }

            throw;
        }

        return slots;
    }

    private bool IsAligned()
    {
        var alignment = _device.Adapter.HostPointerAlignment;
        if (_reader.SlotSize % alignment != 0)
        {
            return false;
        }

        for (var slot = 0; slot < _reader.SlotCount; slot++)
        {
            if (_reader.SlotAddress(slot) % alignment != 0)
            {
                return false;
            }
        }

        return true;
    }

    private void RecordCopy(IGraphicsDevice device, CommandBuffer commandBuffer, VulkanBuffer source, ITexture target)
    {
        device.InsertImageMemoryBarrier(commandBuffer, target,
            AccessFlagBits2.ShaderReadBit, AccessFlagBits2.TransferWriteBit,
            ImageLayout.ShaderReadOnlyOptimal, ImageLayout.TransferDstOptimal,
            PipelineStageFlagBits2.FragmentShaderBit, PipelineStageFlagBits2.AllTransferBit);
        var region = new BufferImageCopy
        {
            BufferRowLength = _reader.Width,
            BufferImageHeight = _reader.Height,
            ImageSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlagBits.ColorBit, LayerCount = 1 },
            ImageOffset = new Offset3D(),
            ImageExtent = new Extent3D { Width = _reader.Width, Height = _reader.Height, Depth = 1 }
        };
        commandBuffer.CopyBufferToImage(source, target.GetImage(), ImageLayout.TransferDstOptimal, 1, region);
        device.InsertImageMemoryBarrier(commandBuffer, target,
            AccessFlagBits2.TransferWriteBit, AccessFlagBits2.ShaderReadBit,
            ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.FragmentShaderBit);
    }

    private void GiveBackCopied()
    {
        if (_copiedFrame > _givenBack)
        {
            _reader.GiveBack(_copiedFrame);
            _givenBack = _copiedFrame;
        }
    }

    private ulong CopiesDone()
    {
        _device.LogicalDevice.GetSemaphoreCounterValue(_copies, out var value);
        return value;
    }

    private void WaitForCopies()
    {
        if (_copyValue == 0 || CopiesDone() >= _copyValue)
        {
            return;
        }

        var wait = new SemaphoreWaitInfo
        {
            SemaphoreCount = 1,
            PSemaphores = new[] { _copies },
            PValues = new[] { _copyValue }
        };
        _device.MainDevice.Observe(_device.LogicalDevice.WaitSemaphores(wait, ulong.MaxValue));
    }
}
