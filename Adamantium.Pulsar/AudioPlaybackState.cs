namespace Adamantium.Pulsar;

public enum AudioPlaybackState
{
    /// <summary>Not playing; it plays from <see cref="AudioPlayback.Position"/> once told to.</summary>
    Stopped,

    Playing,

    Paused,

    /// <summary>Told to play a station, it connects, and gathers enough to play without a gap: it plays once it has.
    /// </summary>
    Connecting,
}
