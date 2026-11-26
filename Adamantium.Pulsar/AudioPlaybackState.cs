namespace Adamantium.Pulsar;

public enum AudioPlaybackState
{
    /// <summary>Not playing; it plays from <see cref="AudioPlayback.Position"/> once told to.</summary>
    Stopped,

    Playing,

    Paused,
}
