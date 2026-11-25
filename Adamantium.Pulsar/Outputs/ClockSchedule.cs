namespace Adamantium.Pulsar.Outputs;

/// <summary>What a device would have played by a time, and so what an output without one asks for: half the buffer at a
/// time. Time is told to it, from the start of the output.</summary>
internal sealed class ClockSchedule
{
    private readonly AudioFormat _format;
    private long _written;

    public ClockSchedule(AudioFormat format, TimeSpan buffer)
    {
        _format = format;
        BufferFrames = Math.Max(2, format.FramesIn(buffer));
        PeriodFrames = BufferFrames / 2;
    }

    public long BufferFrames { get; }

    public long PeriodFrames { get; }

    /// <summary>How many periods to render now; <paramref name="ranDry"/> where there was nothing left to play, and a
    /// device would have played silence.</summary>
    public int PeriodsAt(TimeSpan elapsed, out bool ranDry)
    {
        var played = _format.FramesIn(elapsed);
        ranDry = _written < played;
        if (ranDry)
        {
            _written = played;
        }

        var periods = (int)((BufferFrames - (_written - played)) / PeriodFrames);
        _written += periods * PeriodFrames;
        return periods;
    }

    /// <summary>How long until there is room for another period.</summary>
    public TimeSpan WaitAt(TimeSpan elapsed)
    {
        // A wait counts whole milliseconds and drops the rest: one more, or a thread would spin through it.
        var left = _format.DurationOf(_written - BufferFrames + PeriodFrames) - elapsed;
        return (left > TimeSpan.Zero ? left : TimeSpan.Zero) + TimeSpan.FromMilliseconds(1);
    }
}
