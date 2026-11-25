namespace Adamantium.Pulsar;

/// <summary>What an <see cref="AudioEngine"/> plays: one channel or two, whole in memory (<see cref="SoundData"/>) or read
/// while it plays (<see cref="StreamedSound"/>).</summary>
public abstract class Sound
{
    // A position has 32 bits for the fraction of a frame.
    private const int MostFrames = 1 << 30;

    private protected Sound(AudioFormat format, long frames, SoundLoop? loop)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(format.SampleRate, nameof(format));
        if (format.Channels is not (1 or 2))
        {
            throw new ArgumentException($"A sound has one channel or two, not {format.Channels}.", nameof(format));
        }

        if (frames is < 1 or > MostFrames)
        {
            throw new ArgumentException($"A sound has from one frame to {MostFrames}, not {frames}.", nameof(frames));
        }

        Format = format;
        Frames = (int)frames;
        Loop = loop ?? new SoundLoop(0, Frames);
        if (Loop.Start < 0 || Loop.End > Frames || Loop.Start >= Loop.End)
        {
            throw new ArgumentOutOfRangeException(nameof(loop), Loop, "The loop is not within the sound.");
        }
    }

    public AudioFormat Format { get; }

    public int Frames { get; }

    public TimeSpan Duration => Format.DurationOf(Frames);

    /// <summary>What repeats where it is looped; the whole of it if not said.</summary>
    public SoundLoop Loop { get; }
}
