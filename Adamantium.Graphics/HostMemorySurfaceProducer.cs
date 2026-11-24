using System;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;

namespace Adamantium.Graphics;

/// <summary>An <see cref="ISharedSurfaceProducer"/> for a consumer that shares no GPU memory with this process - on
/// another GPU, or on a driver that shares none between processes: each frame is copied by the GPU into memory shared
/// through a file (<see cref="SharedHandleType.HostMemory"/>), into the slot the consumer read last. Once the copy is
/// done the slot's counter says which frame it holds, and its size; the consumer writes the frame it read. The ring's
/// slots are as large as the largest screen's frame, so a new size of the frames is only new descriptors of the same
/// memory, handed over with its first frame in it; a frame larger than that gets a new ring.</summary>
public sealed class HostMemorySurfaceProducer : ISharedSurfaceProducer
{
    private readonly ISharedSurfaceConsumer _consumer;
    private readonly SurfaceFormat _format;
    private readonly uint _slotCount;
    private readonly ulong _largestFrame;
    private HostFrameRing _ring;
    private SharedSurfaceDescriptor[] _descriptors;
    private uint _width;
    private uint _height;
    private HostFrameRing _pendingRing;
    private uint _pendingWidth;
    private uint _pendingHeight;
    private ulong _resizeFrame;
    private ulong _timelineValue;
    private ulong _lastFrame;
    private ulong _lastAnnounced;
    private ulong[] _written = [];
    private ulong[] _doneAt = [];
    private uint[] _widths = [];
    private uint[] _heights = [];

    /// <summary>Copies frames for <paramref name="consumer"/> into a ring whose slots hold a frame of
    /// <paramref name="largestWidth"/> by <paramref name="largestHeight"/> - the largest screen the consumer shows them
    /// on; a frame larger than that gets a ring of half again its size.</summary>
    public HostMemorySurfaceProducer(ISharedSurfaceConsumer consumer, SurfaceFormat format, uint largestWidth = 0,
        uint largestHeight = 0, uint slotCount = 3)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _format = format;
        _slotCount = slotCount;
        _largestFrame = HostFrameRing.FrameBytes(largestWidth, largestHeight, format);
    }

    /// <summary>Whether <paramref name="device"/> copies frames into memory of the process.</summary>
    public static bool CanProduce(IGraphicsDevice device) => HostSlotBuffer.CanImport(device);

    public void Produce(IGraphicsDevice device, ITexture frame, uint width, uint height, bool inDevice)
    {
        AnnounceDone();
        HandOverPendingRing();
        if (width == 0 || height == 0 || _pendingRing != null || _resizeFrame != 0)
        {
            return;
        }

        if (_ring == null || !_ring.Fits(width, height))
        {
            StartRing(device, frame, width, height);
            _consumer.AskFrame();
            return;
        }

        var slot = FreeSlot();
        if (slot < 0)
        {
            return;
        }

        CopyInto(device, frame, (uint)slot, width, height);
        if (width != _width || height != _height)
        {
            _resizeFrame = _lastFrame;
            _consumer.AskFrame();
        }
    }

    public bool HandsOverWithNextFrame => _pendingRing != null || _resizeFrame != 0;

    public void NoteSubmitted()
    {
        AnnounceDone();
        if (_pendingRing != null || _resizeFrame != 0 || HasCopyInFlight())
        {
            _consumer.AskFrame();
        }
    }

    public bool WaitUntilShown(TimeSpan timeout) => true;

    public void Release(bool retire)
    {
        if (_descriptors != null)
        {
            _consumer.Unbind(_descriptors[0]);
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
        _descriptors = null;
        _resizeFrame = 0;
    }

    public void Dispose() => Release(retire: true);

    private void CopyInto(IGraphicsDevice device, ITexture frame, uint slot, uint width, uint height)
    {
        _ring.RecordCopy(frame, slot, width, height);
        _timelineValue++;
        device.AddSignalSemaphore(_ring.Timeline, _timelineValue);
        _lastFrame++;
        _written[slot] = _lastFrame;
        _doneAt[slot] = _timelineValue;
        _widths[slot] = width;
        _heights[slot] = height;
    }

    // The copies done are said in the slots; a frame of the size bound is announced, and the first frame of a new size,
    // once done, hands over that size's descriptors.
    private void AnnounceDone()
    {
        if (_ring == null)
        {
            return;
        }

        var done = _ring.DoneValue;
        var newest = 0UL;
        var resized = false;
        for (uint slot = 0; slot < _written.Length; slot++)
        {
            if (_doneAt[slot] == 0 || _doneAt[slot] > done)
            {
                continue;
            }

            _ring.Finish(slot, _written[slot], _widths[slot], _heights[slot]);
            _doneAt[slot] = 0;
            if (_written[slot] == _resizeFrame)
            {
                resized = true;
            }
            else if (_widths[slot] == _width && _heights[slot] == _height)
            {
                newest = Math.Max(newest, _written[slot]);
            }
        }

        if (newest > _lastAnnounced)
        {
            _lastAnnounced = newest;
            _descriptors[0].NoteSubmitted(newest);
            _consumer.NoteFrameSubmitted();
        }

        if (resized)
        {
            var slot = Array.IndexOf(_written, _resizeFrame);
            Bind(_widths[slot], _heights[slot], _resizeFrame);
            _resizeFrame = 0;
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

    // Half again as large as a frame past the largest screen's - a window across two of them, or no screen told - so a
    // window still growing is not a new ring at each step.
    private void StartRing(IGraphicsDevice device, ITexture frame, uint width, uint height)
    {
        var frameBytes = HostFrameRing.FrameBytes(width, height, _format);
        var capacity = frameBytes <= _largestFrame ? _largestFrame : frameBytes + frameBytes / 2;
        _pendingRing = new HostFrameRing(device, capacity, _format, _slotCount);
        _pendingRing.RecordCopy(frame, 0, width, height);
        device.AddSignalSemaphore(_pendingRing.Timeline, 1);
        _pendingWidth = width;
        _pendingHeight = height;
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
        _ring.Finish(0, 1, _pendingWidth, _pendingHeight);
        _timelineValue = 1;
        _lastFrame = 1;
        _written = new ulong[_slotCount];
        _doneAt = new ulong[_slotCount];
        _widths = new uint[_slotCount];
        _heights = new uint[_slotCount];
        _written[0] = 1;
        _widths[0] = _pendingWidth;
        _heights[0] = _pendingHeight;
        Bind(_pendingWidth, _pendingHeight, 1);
        Retire(replaced);
    }

    // The ring's frames of this size handed to the consumer, with the first of them in it.
    private void Bind(uint width, uint height, ulong firstFrame)
    {
        _width = width;
        _height = height;
        _descriptors = _ring.Describe(width, height);
        _lastAnnounced = firstFrame;
        _descriptors[0].NoteSubmitted(firstFrame);
        _consumer.Bind(_descriptors[0]);
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
