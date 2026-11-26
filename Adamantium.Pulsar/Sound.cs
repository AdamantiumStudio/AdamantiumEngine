namespace Adamantium.Pulsar;

/// <summary>What an <see cref="AudioEngine"/> plays: up to <see cref="AudioFormat.MostChannels"/> channels, whole in
/// memory (<see cref="SoundData"/>) or read while it plays (<see cref="StreamedSound"/>). More than two are mixed down to
/// two.</summary>
public abstract class Sound
{
    // A position has 32 bits for the fraction of a frame.
    internal const int MostFrames = 1 << 30;

    private protected Sound(AudioFormat format, long frames, SoundLoop? loop)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(format.SampleRate, nameof(format));
        if (format.Channels is < 1 or > AudioFormat.MostChannels)
        {
            throw new ArgumentException($"A sound has from one channel to {AudioFormat.MostChannels}, not {format.Channels}.",
                nameof(format));
        }

        if (frames is < 1 or > MostFrames)
        {
            throw new ArgumentException($"A sound has from one frame to {MostFrames}, not {frames}.", nameof(frames));
        }

        Format = format;
        Downmix = Mixing.Downmix.GainsOf(format);
        Frames = (int)frames;
        Loop = loop ?? new SoundLoop(0, Frames);
        if (Loop.Start < 0 || Loop.End > Frames || Loop.Start >= Loop.End)
        {
            throw new ArgumentOutOfRangeException(nameof(loop), Loop, "The loop is not within the sound.");
        }
    }

    public AudioFormat Format { get; }

    /// <summary>The left and the right gain of each channel; null for one, and for the front two.</summary>
    internal float[] Downmix { get; }

    public int Frames { get; }

    public TimeSpan Duration => Format.DurationOf(Frames);

    /// <summary>What repeats where it is looped; the whole of it if not said.</summary>
    public SoundLoop Loop { get; }

    /// <summary>Of the file it was read from, its codec (<see cref="ISoundDecoder.Codec"/>); null for samples made
    /// otherwise.</summary>
    public string Codec { get; internal init; }

    /// <summary>What the file it was read from says of it.</summary>
    public SoundTags Tags { get; internal init; }
}
