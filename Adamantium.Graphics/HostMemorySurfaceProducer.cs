using System;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;

namespace Adamantium.Graphics;

/// <summary>An <see cref="ISharedSurfaceProducer"/> for a consumer that shares no GPU memory with this process - on
/// another GPU, or on a driver that shares none between processes: each frame is copied by the GPU into memory shared
/// through a file (<see cref="SharedHandleType.HostMemory"/>), into the slot the consumer read last. Once the copy is
/// done the slot's counter says which frame it holds; the consumer writes the frame it read. A new size gets a new ring,
/// handed over with its first frame in it.</summary>
public sealed class HostMemorySurfaceProducer : ISharedSurfaceProducer
{
    private readonly ISharedSurfaceConsumer _consumer;
    private readonly SurfaceFormat _format;
    private readonly uint _slotCount;
    private HostFrameRing _ring;
    private HostFrameRing _pendingRing;
    private ulong _timelineValue;
    private ulong _lastFrame;
    private ulong _lastAnnounced;
    private ulong[] _written = [];
    private ulong[] _doneAt = [];

    public HostMemorySurfaceProducer(ISharedSurfaceConsumer consumer, SurfaceFormat format, uint slotCount = 3)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _format = format;
        _slotCount = slotCount;
    }

    /// <summary>Whether <paramref name="device"/> copies frames into memory of the process.</summary>
    public static bool CanProduce(IGraphicsDevice device) => HostFrameRing.CanImport(device);

    public void Produce(IGraphicsDevice device, ITexture frame, uint width, uint height, bool inDevice)
    {
        AnnounceDone();
        HandOverPendingRing();
        if (StartRingForNewSize(device, frame, width, height))
        {
            _consumer.AskFrame();
            return;
        }

        if (_ring == null || _pendingRing != null)
        {
            return;
        }

        var slot = FreeSlot();
        if (slot < 0)
        {
            return;
        }

        _ring.RecordCopy(frame, (uint)slot);
        _timelineValue++;
        device.AddSignalSemaphore(_ring.Timeline, _timelineValue);
        _lastFrame++;
        _written[slot] = _lastFrame;
        _doneAt[slot] = _timelineValue;
    }

    public void NoteSubmitted()
    {
        AnnounceDone();
        if (_pendingRing != null || HasCopyInFlight())
        {
            _consumer.AskFrame();
        }
    }

    public bool WaitUntilShown(TimeSpan timeout) => true;

    public void Release(bool retire)
    {
        if (_ring != null)
        {
            _consumer.Unbind(_ring.Descriptors[0]);
        }

        if (retire)
        {
            Retire(_ring);
            Retire(_pendingRing);
        }
        else
        {
            _ring?.Dispose();
            _pendingRing?.Dispose();
        }

        _ring = null;
        _pendingRing = null;
    }

    public void Dispose() => Release(retire: true);

    private void AnnounceDone()
    {
        if (_ring == null)
        {
            return;
        }

        var done = _ring.DoneValue;
        var newest = 0UL;
        for (uint slot = 0; slot < _written.Length; slot++)
        {
            if (_doneAt[slot] == 0 || _doneAt[slot] > done)
            {
                continue;
            }

            _ring.Memory.WriteCounter(_ring.Memory.CounterOffset(slot), _written[slot]);
            _doneAt[slot] = 0;
            newest = Math.Max(newest, _written[slot]);
        }

        if (newest > _lastAnnounced)
        {
            _lastAnnounced = newest;
            _ring.Descriptors[0].NoteSubmitted(newest);
            _consumer.NoteFrameSubmitted();
        }
    }

    private bool HasCopyInFlight()
    {
        foreach (var doneAt in _doneAt)
        {
            if (doneAt != 0)
            {
                return true;
            }
        }

        return false;
    }

    private int FreeSlot()
    {
        var free = -1;
        for (uint slot = 0; slot < _written.Length; slot++)
        {
            var read = _ring.Memory.ReadCounter(_ring.Memory.CounterOffset(slot) + sizeof(ulong));
            if (_doneAt[slot] != 0 || read < _written[slot])
            {
                continue;
            }

            if (free < 0 || _written[slot] < _written[free])
            {
                free = (int)slot;
            }
        }

        return free;
    }

    private bool StartRingForNewSize(IGraphicsDevice device, ITexture frame, uint width, uint height)
    {
        if (_pendingRing != null)
        {
            return true;
        }

        if (width == 0 || height == 0 || _ring != null && _ring.Width == width && _ring.Height == height)
        {
            return false;
        }

        _pendingRing = new HostFrameRing(device, width, height, _format, _slotCount);
        _pendingRing.RecordCopy(frame, 0);
        device.AddSignalSemaphore(_pendingRing.Timeline, 1);
        return true;
    }

    private void HandOverPendingRing()
    {
        if (_pendingRing == null || _pendingRing.DoneValue == 0)
        {
            return;
        }

        var replaced = _ring;
        _ring = _pendingRing;
        _pendingRing = null;
        _ring.Memory.WriteCounter(_ring.Memory.CounterOffset(0), 1);
        _timelineValue = 1;
        _lastFrame = 1;
        _lastAnnounced = 1;
        _written = new ulong[_slotCount];
        _doneAt = new ulong[_slotCount];
        _written[0] = 1;
        _ring.Descriptors[0].NoteSubmitted(1);
        _consumer.Bind(_ring.Descriptors[0]);
        Retire(replaced);
    }

    private static void Retire(HostFrameRing ring)
    {
        if (ring == null)
        {
            return;
        }

        ring.Device.AddToDeferDisposeQueue(ring);
    }
}
