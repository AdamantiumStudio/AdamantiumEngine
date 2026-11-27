using System.Numerics;

namespace Adamantium.Pulsar;

/// <summary>How samples are laid out: 32-bit floats, the channels of a frame side by side, in the order of their
/// <see cref="Speakers"/>.</summary>
public readonly record struct AudioFormat
{
    /// <summary>A file that says a higher one is damaged.</summary>
    internal const int HighestRate = 1_000_000;

    /// <summary>The most channels a sound has: FLAC's most, and 7.1.</summary>
    public const int MostChannels = 8;

    private const Speakers AllSpeakers = (Speakers)0x3FFFF;

    /// <param name="sampleRate">Frames a second.</param>
    /// <param name="channels">How many channels a frame has.</param>
    /// <param name="speakers">One for each channel; where not said, the usual of their number.</param>
    public AudioFormat(int sampleRate, int channels, Speakers speakers = Speakers.None)
    {
        if (speakers != Speakers.None && !Fits(speakers, channels))
        {
            throw new ArgumentException($"{channels} channels are not played by {speakers}.", nameof(speakers));
        }

        SampleRate = sampleRate;
        Channels = channels;
        Speakers = speakers == Speakers.None ? UsualFor(channels) : speakers;
    }

    /// <summary>What Pulsar mixes in.</summary>
    public static AudioFormat Default => new(48000, 2);

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>None for a number of channels that has no usual speakers.</summary>
    public Speakers Speakers { get; }

    /// <summary>The usual speakers of <paramref name="channels"/>: those of FLAC, and of Windows for 5.1 and 7.1. None
    /// for more than <see cref="MostChannels"/>.</summary>
    public static Speakers UsualFor(int channels) => channels switch
    {
        1 => Speakers.FrontCenter,
        2 => Front,
        3 => Front | Speakers.FrontCenter,
        4 => Front | Back,
        5 => Front | Speakers.FrontCenter | Back,
        6 => Front | Speakers.FrontCenter | Speakers.LowFrequency | Back,
        7 => Front | Speakers.FrontCenter | Speakers.LowFrequency | Speakers.BackCenter | Side,
        8 => Front | Speakers.FrontCenter | Speakers.LowFrequency | Back | Side,
        _ => Speakers.None,
    };

    /// <summary>Whether <paramref name="speakers"/> are known ones, one for each channel.</summary>
    public static bool Fits(Speakers speakers, int channels) =>
        (speakers & ~AllSpeakers) == 0 && BitOperations.PopCount((uint)speakers) == channels;

    private static Speakers Front => Speakers.FrontLeft | Speakers.FrontRight;

    private static Speakers Back => Speakers.BackLeft | Speakers.BackRight;

    private static Speakers Side => Speakers.SideLeft | Speakers.SideRight;

    // 128 bits between: at 48 kHz 64 overflow after 222 days.
    public TimeSpan DurationOf(long frames) =>
        TimeSpan.FromTicks((long)((Int128)frames * TimeSpan.TicksPerSecond / SampleRate));

    public long FramesIn(TimeSpan duration) => (long)((Int128)duration.Ticks * SampleRate / TimeSpan.TicksPerSecond);
}
