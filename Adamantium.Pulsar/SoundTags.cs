namespace Adamantium.Pulsar;

/// <summary>What a sound file says of itself: from Vorbis comments (FLAC, Vorbis, Opus), ID3 (MP3) or a WAV file's
/// LIST INFO chunk. Each is null where the file does not say it.</summary>
public readonly record struct SoundTags(string Title, string Artist, string Album)
{
    /// <summary>Whether it says anything.</summary>
    public bool IsEmpty => Title == null && Artist == null && Album == null;
}
