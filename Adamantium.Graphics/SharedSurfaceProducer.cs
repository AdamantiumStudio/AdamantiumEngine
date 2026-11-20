using System;
using System.Diagnostics;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;

namespace Adamantium.Graphics;

/// <summary>Sends an output's frames to an <see cref="ISharedSurfaceConsumer"/> through a ring of
/// <see cref="SharedSurface"/>s: each frame is copied into the surface the consumer gave back last; a new size gets a new
/// ring, handed to the consumer once it holds its first frame. Driven from the thread that draws the output.</summary>
public sealed class SharedSurfaceProducer : IDisposable
{
    private readonly ISharedSurfaceConsumer _consumer;
    private readonly SurfaceFormat _format;
    private readonly uint _bufferCount;
    private SharedSurface _surface;
    private SharedSurface _pendingSurface;
    private ulong _lastProduced;
    private ulong[] _written = [];
    private ulong _demandSeen;
    private Action _askFrame;
    private SharedSurfaceDescriptor _unannouncedDescriptor;
    private ulong _unannouncedFrame;

    /// <summary>A producer for <paramref name="consumer"/>, in <paramref name="format"/>, rotating through
    /// <paramref name="bufferCount"/> surfaces: with three, the frame drawn on the consumer's ask goes to the surface of
    /// two frames back, not to the one the consumer is still copying.</summary>
    public SharedSurfaceProducer(ISharedSurfaceConsumer consumer, SurfaceFormat format, uint bufferCount = 3)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _format = format;
        _bufferCount = bufferCount;
    }

    internal SharedSurface Surface => _surface;

    /// <summary>Copies <paramref name="frame"/>, <paramref name="width"/> × <paramref name="height"/>, into the ring, on
    /// <paramref name="device"/>'s queue after what drew it. <paramref name="inDevice"/>: the consumer draws with this
    /// device's own logical device, so the surfaces need no OS handles. A new size or way of sharing starts a new ring and
    /// asks for another frame. Followed by <see cref="NoteSubmitted"/> once the device's frame is submitted.</summary>
    public void Produce(IGraphicsDevice device, ITexture frame, uint width, uint height, bool inDevice)
    {
        HandOverPendingSurface();
        if (StartSurfaceForNewSize(device, frame, width, height, inDevice))
        {
            _consumer.AskFrame();
            return;
        }

        if (_surface == null || _pendingSurface != null)
        {
            return;
        }

        var index = FreeSurface();
        if (index < 0)
        {
            return;
        }

        var target = _surface.SurfaceAt(index);
        device.AddWaitSemaphore(target.ConsumeSemaphore, PipelineStageFlagBits2.AllTransferBit, _written[index]);
        device.RecordSharedSurfaceCopy(frame, target);
        _lastProduced++;
        _written[index] = _lastProduced;
        device.AddSignalSemaphore(target.ProduceSemaphore, _lastProduced);
        _unannouncedDescriptor = _surface.Descriptor;
        _unannouncedFrame = _lastProduced;
    }

    /// <summary>Tells the consumer of the frame <see cref="Produce"/> copied, once the device's frame is submitted.</summary>
    public void NoteSubmitted()
    {
        if (_unannouncedDescriptor == null)
        {
            return;
        }

        _unannouncedDescriptor.NoteSubmitted(_unannouncedFrame);
        _unannouncedDescriptor = null;
        _consumer.NoteFrameSubmitted();
    }

    /// <summary>Waits until the consumer asks for a frame, so one is drawn per frame it shows, then until the oldest
    /// surface is given back for it, up to <paramref name="timeout"/>. Gives whether it was; if not, asks for a frame
    /// again on the consumer's next ask.</summary>
    public bool WaitUntilShown(TimeSpan timeout)
    {
        if (_surface == null || _pendingSurface != null)
        {
            return true;
        }

        var start = Stopwatch.GetTimestamp();
        _demandSeen = _surface.Descriptor.Demand.WaitBeyond(_demandSeen, timeout);
        var left = timeout - Stopwatch.GetElapsedTime(start);
        var oldest = OldestSurface();
        var shown = _surface.SurfaceAt(oldest).WaitConsumed(_written[oldest], left > TimeSpan.Zero ? left : TimeSpan.Zero);
        if (!shown)
        {
            _surface.Descriptor.Demand.WhenBeyond(_demandSeen, _askFrame ??= _consumer.AskFrame);
        }

        return shown;
    }

    /// <summary>Lets the consumer go of the ring and frees it: <paramref name="retire"/> once the device's frames still
    /// in flight are done with it - their submits use its semaphores - else at once, for a device that is going away.
    /// A ring replaced by a new size is retired the same way, after the consumer is bound to the new one.</summary>
    public void Release(bool retire)
    {
        if (_surface != null)
        {
            _consumer.Unbind(_surface.Descriptor);
        }

        if (retire)
        {
            Retire(_surface);
            Retire(_pendingSurface);
        }
        else
        {
            _surface?.Dispose();
            _pendingSurface?.Dispose();
        }

        _surface = null;
        _pendingSurface = null;
    }

    public void Dispose() => Release(retire: true);

    private int FreeSurface()
    {
        var oldest = OldestSurface();
        return _surface.SurfaceAt(oldest).ConsumeValue >= _written[oldest] ? oldest : -1;
    }

    private int OldestSurface()
    {
        var oldest = 0;
        for (var i = 1; i < _written.Length; i++)
        {
            if (_written[i] < _written[oldest])
            {
                oldest = i;
            }
        }

        return oldest;
    }

    private bool StartSurfaceForNewSize(IGraphicsDevice device, ITexture frame, uint width, uint height, bool inDevice)
    {
        if (_pendingSurface != null)
        {
            return true;
        }

        if (width == 0 || height == 0)
        {
            return false;
        }

        if (_surface != null && _surface.Width == width && _surface.Height == height
            && (_surface.Descriptor.DeviceSurface != null) == inDevice)
        {
            return false;
        }

        _pendingSurface = inDevice
            ? SharedSurface.CreateInDevice(device, width, height, _format, bufferCount: _bufferCount)
            : SharedSurface.CreateExportable(device, width, height, _format, bufferCount: _bufferCount);
        device.RecordSharedSurfaceCopy(frame, _pendingSurface);
        device.AddSignalSemaphore(_pendingSurface.ProduceSemaphore, 1);
        _unannouncedDescriptor = _pendingSurface.Descriptor;
        _unannouncedFrame = 1;
        return true;
    }

    private void HandOverPendingSurface()
    {
        if (_pendingSurface == null || _pendingSurface.ProduceValue == 0)
        {
            return;
        }

        var replaced = _surface;
        _surface = _pendingSurface;
        _pendingSurface = null;
        _lastProduced = 1;
        _written = new ulong[_surface.BufferCount];
        _written[0] = 1;
        _demandSeen = 0;
        _consumer.Bind(_surface.Descriptor);
        Retire(replaced);
    }

    private static void Retire(SharedSurface surface)
    {
        surface?.GraphicsDevice.AddToDeferDisposeQueue(surface);
    }
}
