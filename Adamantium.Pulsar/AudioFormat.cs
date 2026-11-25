namespace Adamantium.Pulsar;

/// <summary>How samples are laid out: 32-bit floats, the channels of a frame side by side.</summary>
public readonly record struct AudioFormat(int SampleRate, int Channels)
{
    /// <summary>What Pulsar mixes in.</summary>
    public static AudioFormat Default => new(48000, 2);

    // 128 bits between: at 48 kHz 64 overflow after 222 days.
    public TimeSpan DurationOf(long frames) =>
        TimeSpan.FromTicks((long)((Int128)frames * TimeSpan.TicksPerSecond / SampleRate));

    public long FramesIn(TimeSpan duration) => (long)((Int128)duration.Ticks * SampleRate / TimeSpan.TicksPerSecond);
}
