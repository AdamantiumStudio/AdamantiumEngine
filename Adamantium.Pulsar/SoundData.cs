namespace Adamantium.Pulsar;

/// <summary>A sound in memory: samples of one or two channels. They must not be changed once an engine has them.</summary>
public sealed class SoundData
{
    // A position has 32 bits for the fraction of a frame.
    private const int MostFrames = 1 << 30;

    /// <param name="loop">What repeats where it is looped; the whole of it if not said.</param>
    public SoundData(AudioFormat format, float[] samples, SoundLoop? loop = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(format.SampleRate, nameof(format));
        if (format.Channels is not (1 or 2))
        {
            throw new ArgumentException($"A sound has one channel or two, not {format.Channels}.", nameof(format));
        }

        if (samples.Length == 0 || samples.Length % format.Channels != 0 || samples.Length / format.Channels > MostFrames)
        {
            throw new ArgumentException($"A sound is whole frames, from one to {MostFrames}.", nameof(samples));
        }

        Format = format;
        Samples = samples;
        Frames = samples.Length / format.Channels;
        Loop = loop ?? new SoundLoop(0, Frames);
        if (Loop.Start < 0 || Loop.End > Frames || Loop.Start >= Loop.End)
        {
            throw new ArgumentOutOfRangeException(nameof(loop), Loop, "The loop is not within the sound.");
        }
    }

    public AudioFormat Format { get; }

    public int Frames { get; }

    public TimeSpan Duration => Format.DurationOf(Frames);

    public SoundLoop Loop { get; }

    internal float[] Samples { get; }

    /// <summary>The samples, -1 to 1, the channels of a frame side by side.</summary>
    public ReadOnlySpan<float> AsSpan() => Samples;
}
