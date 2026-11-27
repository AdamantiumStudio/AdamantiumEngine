namespace Adamantium.Pulsar.Live;

/// <summary>A station's sound as the engine plays it: of no length, though a voice of it ends after
/// <see cref="Sound.MostFrames"/> frames - six hours at 48 kHz - and the next goes on with what comes. Its voice takes
/// what its <see cref="LiveFeed"/> has.</summary>
internal sealed class LiveSound : Sound
{
    public LiveSound(LiveFeed feed)
        : base(feed.Format, MostFrames, null)
    {
        Feed = feed;
        Codec = feed.Codec;
    }

    public LiveFeed Feed { get; }

    internal override bool IsStreamed => true;

    internal override bool ComesOn => !Feed.HasEnded;

    internal override ISoundDecoder OpenDecoder() => new LiveFeedReader(Feed);
}
