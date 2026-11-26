namespace Adamantium.Pulsar;

/// <summary>What a player shows of a sound file.</summary>
/// <param name="Codec">As people name it: "WAV", "FLAC", "MP3", "Vorbis" or "Opus".</param>
/// <param name="Format">Its own, before it is mixed.</param>
/// <param name="Duration">Zero where the file does not say it, as a damaged one may not.</param>
/// <param name="Bitrate">Bits a second, of the whole file over its duration: as players show it; 0 where that is not
/// known.</param>
public sealed record SoundInfo(string Codec, AudioFormat Format, TimeSpan Duration, int Bitrate, SoundTags Tags)
{
    /// <inheritdoc cref="Read(ISoundStorage)"/>
    public static SoundInfo Read(string path) => Read(new SoundFileStorage(path));

    /// <summary>Without decoding its samples, for a list of files to say what each is: what the file says of itself, and
    /// of an MP3 its frame headers, counted to the end.</summary>
    /// <exception cref="InvalidDataException">It is no sound file, or one that can not be read.</exception>
    public static SoundInfo Read(ISoundStorage storage) => Read(storage, out _);

    /// <param name="length">The file's bytes.</param>
    internal static SoundInfo Read(ISoundStorage storage, out long length)
    {
        ArgumentNullException.ThrowIfNull(storage);
        var stream = storage.Open();
        try
        {
            length = stream.Length - stream.Position;
        }
        catch
        {
            stream.Dispose();
            throw;
        }

        using var decoder = SoundFile.Open(stream);
        var duration = decoder.Frames > 0 ? decoder.Format.DurationOf(decoder.Frames) : TimeSpan.Zero;
        return new SoundInfo(decoder.Codec, decoder.Format, duration, BitrateOf(length, duration), decoder.Tags);
    }

    internal static int BitrateOf(long length, TimeSpan duration) =>
        duration > TimeSpan.Zero ? (int)Math.Round(length * 8 / duration.TotalSeconds) : 0;
}
