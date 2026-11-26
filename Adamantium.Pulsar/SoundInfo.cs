namespace Adamantium.Pulsar;

/// <summary>What a player shows of a sound file.</summary>
/// <param name="Codec">As people name it: "WAV", "FLAC", "MP3", "Vorbis" or "Opus".</param>
/// <param name="Format">Its own, before it is mixed.</param>
/// <param name="Bitrate">Bits a second, of the whole file over its duration: as players show it.</param>
public sealed record SoundInfo(string Codec, AudioFormat Format, TimeSpan Duration, int Bitrate, SoundTags Tags);
