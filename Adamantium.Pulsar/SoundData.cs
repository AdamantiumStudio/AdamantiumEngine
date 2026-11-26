namespace Adamantium.Pulsar;

/// <summary>A sound whole in memory. Its samples must not be changed once an engine has them.</summary>
public sealed class SoundData : Sound
{
    /// <param name="samples">-1 to 1, the channels of a frame side by side.</param>
    /// <param name="loop">What repeats where it is looped; the whole of it if not said.</param>
    public SoundData(AudioFormat format, float[] samples, SoundLoop? loop = null)
        : base(format, FramesOf(format, samples), loop)
    {
        Samples = samples;
    }

    internal float[] Samples { get; }

    /// <summary>The samples, -1 to 1, the channels of a frame side by side.</summary>
    public ReadOnlySpan<float> AsSpan() => Samples;

    private static int FramesOf(AudioFormat format, float[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        return format.Channels > 0 && samples.Length % format.Channels == 0
            ? samples.Length / format.Channels
            : throw new ArgumentException("A sound is whole frames.", nameof(samples));
    }
}
