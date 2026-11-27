namespace Adamantium.Pulsar.Live;

/// <summary>What a voice reads of a station: the samples its feed has, as many as there are, at once - never waiting
/// for the network. Fewer than asked is not the end while the feed goes on.</summary>
internal sealed class LiveFeedReader(LiveFeed feed) : ISoundDecoder
{
    public AudioFormat Format => feed.Format;

    public long Frames => -1;

    public SoundLoop? Loop => null;

    public string Codec => feed.Codec;

    public int Read(Span<float> samples) => feed.Take(samples);

    public void Seek(long frame)
    {
        // A station plays on from where it is: a voice begun again takes what comes next.
    }

    // The feed is the sound's, not the voice's.
    public void Dispose()
    {
    }
}
