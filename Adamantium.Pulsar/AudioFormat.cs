namespace Adamantium.Pulsar;

/// <summary>How samples are laid out: 32-bit floats, the channels of a frame side by side.</summary>
public readonly record struct AudioFormat(int SampleRate, int Channels)
{
    /// <summary>What Pulsar mixes in.</summary>
    public static AudioFormat Default => new(48000, 2);

    public TimeSpan DurationOf(long frames) => TimeSpan.FromTicks(frames * TimeSpan.TicksPerSecond / SampleRate);

    public long FramesIn(TimeSpan duration) => duration.Ticks * SampleRate / TimeSpan.TicksPerSecond;
}
