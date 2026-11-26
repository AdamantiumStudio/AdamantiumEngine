namespace Adamantium.Pulsar.Tests;

/// <summary>What the FLAC files of the tests hold, in integers: a triangle wave of each channel's own, and some noise. They
/// were encoded from it by the reference encoder, flac 1.5.0.</summary>
internal sealed class FlacSignal(int channels, int bits, int frames, int noise = 4)
{
    public int Frames { get; } = frames;

    public float[] ToFloats()
    {
        var samples = new float[Frames * channels];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = At(index / channels, index % channels) / (float)(1L << bits - 1);
        }

        return samples;
    }

    public long At(int frame, int channel)
    {
        var period = 100 + channel * 14;
        var phase = frame % period;
        var half = period / 2;
        var top = (1L << bits - 1) / 2;
        var triangle = (phase < half ? phase : period - phase) * 2 * top / half - top;
        var random = (uint)(frame * 2 + channel) * 2654435761u;
        return triangle + (noise == 0 ? 0 : (random >> 16) % noise);
    }
}
