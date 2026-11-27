namespace Adamantium.Pulsar.Tests;

/// <summary>A decoder that says it has more frames than it gives, as a file cut short does.</summary>
internal sealed class ShortDecoder(ISoundDecoder decoder, long saysFrames) : ISoundDecoder
{
    public AudioFormat Format => decoder.Format;

    public long Frames => saysFrames;

    public SoundLoop? Loop => null;

    public int Read(Span<float> samples) => decoder.Read(samples);

    public void Seek(long frame) => decoder.Seek(frame);

    public void Dispose() => decoder.Dispose();
}
