namespace Adamantium.Pulsar;

/// <summary>A sound read while it plays: music, speech, what is long. A voice of it begins once its first part was read.
/// </summary>
public sealed class StreamedSound : Sound
{
    private StreamedSound(ISoundStorage storage, ISoundDecoder decoder)
        : base(decoder.Format, decoder.Frames, decoder.Loop)
    {
        Storage = storage;
    }

    internal ISoundStorage Storage { get; }

    /// <summary>Reads what the file says of itself, and leaves the samples.</summary>
    /// <exception cref="InvalidDataException">It is no sound file, or one that can not be streamed.</exception>
    public static StreamedSound Open(ISoundStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        using var decoder = SoundFile.Open(storage.Open());
        return decoder.Frames > 0
            ? new StreamedSound(storage, decoder)
            : throw new InvalidDataException("The sound file does not say how long it is: it can not be streamed.");
    }
}
