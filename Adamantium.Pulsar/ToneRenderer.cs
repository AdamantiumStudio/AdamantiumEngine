namespace Adamantium.Pulsar;

/// <summary>A steady sine in every channel.</summary>
public sealed class ToneRenderer(double frequency, float amplitude) : IAudioRenderer
{
    private double _phase;

    public void Render(Span<float> samples, AudioFormat format)
    {
        var step = frequency / format.SampleRate;
        for (var start = 0; start < samples.Length; start += format.Channels)
        {
            samples.Slice(start, format.Channels).Fill(amplitude * (float)Math.Sin(_phase * Math.Tau));
            _phase += step;
            _phase -= Math.Floor(_phase);
        }
    }
}
