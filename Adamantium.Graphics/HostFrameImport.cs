using System;
using Adamantium.Graphics.Core;
using Adamantium.Vulkan.Core;
using VulkanBuffer = Adamantium.Vulkan.Core.Buffer;

namespace Adamantium.Graphics;

/// <summary>A surface's side of a <see cref="SharedHandleType.HostMemory"/> ring on a device: copies the newest frame
/// of the surface's size into it on the GPU - from the ring's slot imported as a buffer where the device takes memory of
/// the process, else through a staging buffer - and gives the slot back once the copy is done. The ring itself is held
/// in <see cref="MainGraphicsDevice.HostFrameRings"/>, shared with the surfaces of its other sizes.</summary>
internal sealed class HostFrameImport : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly ImportedFrameRing _ring;
    private readonly uint _width;
    private readonly uint _height;
    private readonly Buffer _staging;
    private readonly Semaphore _copies;
    private ulong _copyValue;
    private ulong _latest;
    private int _copiedSlot = -1;
    private ulong _copiedFrame;
    private bool _disposed;

    public HostFrameImport(IGraphicsDevice device, SharedSurfaceDescriptor descriptor, bool importSlots = true)
    {
        _device = device;
        _width = descriptor.Width;
        _height = descriptor.Height;
        _ring = device.MainDevice.HostFrameRings.Hold(device, descriptor);
        try
        {
            _copies = device.LogicalDevice.CreateSemaphore(new SemaphoreCreateInfo
            {
                PNext = new SemaphoreTypeCreateInfo { SemaphoreType = SemaphoreType.Timeline, InitialValue = 0 }
            });
            if (!importSlots || _ring.Slots == null)
            {
                _staging = Buffer.New(device, (ulong)_ring.Reader.Pixels(0, _width, _height).Length,
                    BufferUsageFlags.TransferSrc, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Whether the frames are copied from the slots themselves, not through a staging buffer.</summary>
    public bool ImportsSlots => _staging == null;

    /// <summary>Copies the newest frame of the surface's size finished since the last into <paramref name="target"/>,
    /// submitted on its own on <paramref name="device"/> ahead of its frame; gives the frame, or 0 when none is new or
    /// the last copy still runs.</summary>
    public ulong LatchInto(IGraphicsDevice device, ITexture target)
    {
        if (CopiesDone() < _copyValue)
        {
            return 0;
        }

        EndCopy();
        if (!_ring.Reader.TryTakeNewest(_latest, _width, _height, out var slot, out var frame))
        {
            return 0;
        }

        _latest = frame;
        // The older frames are never taken now: their slots go back at once, but those another surface still copies from.
        _ring.GiveBack(frame - 1);
        VulkanBuffer source;
        if (_staging == null)
        {
            source = _ring.Slots[slot].Buffer;
        }
        else
        {
            _staging.SetData(_ring.Reader.Pixels(slot, _width, _height));
            source = _staging;
            _ring.GiveBack(frame);
        }

        var copy = _copyValue + 1;
        var result = device.SubmitAside(commandBuffer => RecordCopy(device, commandBuffer, source, target),
            null, PipelineStageFlagBits2.None, 0, [(_copies, copy)]);
        if (device.MainDevice.Observe(result) != Result.Success)
        {
            return 0;
        }

        _copyValue = copy;
        target.ImageLayout = ImageLayout.ShaderReadOnlyOptimal;
        if (_staging == null)
        {
            _ring.BeginCopy(slot);
            _copiedSlot = slot;
            _copiedFrame = frame;
        }

        return frame;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_copies != null)
        {
            WaitForCopies();
            _device.Destroy(_copies);
        }

        EndCopy();
        _staging?.Dispose();
        _ring.Release();
    }

    // A target sampled since the copy before, or - nothing in it yet, before the first copy - in no layout at all: the
    // copy takes it from there, with no transition of its own submitted and waited for.
    private void RecordCopy(IGraphicsDevice device, CommandBuffer commandBuffer, VulkanBuffer source, ITexture target)
    {
        var sampled = target.ImageLayout == ImageLayout.ShaderReadOnlyOptimal;
        device.InsertImageMemoryBarrier(commandBuffer, target,
            sampled ? AccessFlagBits2.ShaderReadBit : AccessFlagBits2.None, AccessFlagBits2.TransferWriteBit,
            sampled ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.Undefined, ImageLayout.TransferDstOptimal,
            sampled ? PipelineStageFlagBits2.FragmentShaderBit : PipelineStageFlagBits2.None,
            PipelineStageFlagBits2.AllTransferBit);
        var region = new BufferImageCopy
        {
            BufferRowLength = _width,
            BufferImageHeight = _height,
            ImageSubresource = new ImageSubresourceLayers { AspectMask = ImageAspectFlagBits.ColorBit, LayerCount = 1 },
            ImageOffset = new Offset3D(),
            ImageExtent = new Extent3D { Width = _width, Height = _height, Depth = 1 }
        };
        commandBuffer.CopyBufferToImage(source, target.GetImage(), ImageLayout.TransferDstOptimal, 1, region);
        device.InsertImageMemoryBarrier(commandBuffer, target,
            AccessFlagBits2.TransferWriteBit, AccessFlagBits2.ShaderReadBit,
            ImageLayout.TransferDstOptimal, ImageLayout.ShaderReadOnlyOptimal,
            PipelineStageFlagBits2.AllTransferBit, PipelineStageFlagBits2.FragmentShaderBit);
    }

    // The copy from a slot done: the slot, and the older frames no surface still copies from, given back.
    private void EndCopy()
    {
        if (_copiedSlot < 0)
        {
            return;
        }

        _ring.EndCopy(_copiedSlot, _copiedFrame);
        _copiedSlot = -1;
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
