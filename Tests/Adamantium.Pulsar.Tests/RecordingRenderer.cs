namespace Adamantium.Pulsar.Tests;

/// <summary>Counts what an output asks of it, and does what a test wants done inside a call.</summary>
internal sealed class RecordingRenderer : IAudioRenderer
{
    private long _frames;
    private long _allocated;

    public long Frames => Volatile.Read(ref _frames);

    /// <summary>The bytes the output's thread allocated between two calls, after the first ones.</summary>
    public long AllocatedBetweenCalls => Volatile.Read(ref _allocated);

    /// <summary>Done at the call it counts down to.</summary>
    public Action AtCall { get; init; }

    /// <summary>Or done once, at the call that makes it this many frames: how many a call asks for is the device's.</summary>
    public long AfterFrames { get; init; } = long.MaxValue;

    private bool _isAfter;

    public int Call { get; init; } = int.MaxValue;

    private int _calls;
    private long _allocatedBefore;

    public void Render(Span<float> samples, AudioFormat format)
    {
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        if (++_calls > 5)
        {
            _allocated += allocated - _allocatedBefore;
        }

        samples.Clear();
        Volatile.Write(ref _frames, _frames + samples.Length / format.Channels);
        if (_calls == Call || (!_isAfter && _frames >= AfterFrames))
        {
            _isAfter = true;
            AtCall();
        }

        _allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    }
}
