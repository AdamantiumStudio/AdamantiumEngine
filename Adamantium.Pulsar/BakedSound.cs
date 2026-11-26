namespace Adamantium.Pulsar;

/// <summary>A sound as a content build keeps it: how it is to be played, and its file as it was.</summary>
public static class BakedSound
{
    private const int HeaderLength = 8;
    private const byte Version = 1;
    private const byte StreamedFlag = 1;

    /// <summary>A sound longer than this is streamed where it is not said whether it is.</summary>
    public static TimeSpan LongestInMemory { get; } = TimeSpan.FromSeconds(10);

    public static void Write(Stream stream, byte[] soundFile, bool isStreamed)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(soundFile);
        stream.Write([.. "ASND"u8, Version, isStreamed ? StreamedFlag : (byte)0, 0, 0]);
        stream.Write(soundFile);
    }

    /// <summary>The sound in <paramref name="storage"/>, as it was written: whole in memory, or read while it plays.</summary>
    /// <exception cref="InvalidDataException">It is none, or one that can not be read.</exception>
    public static Sound Read(ISoundStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        using var stream = storage.Open();
        var flags = ReadHeader(stream);
        return (flags & StreamedFlag) != 0 ? StreamedSound.Open(new Storage(storage)) : SoundFile.Read(stream);
    }

    private static byte ReadHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length || !header.StartsWith("ASND"u8))
        {
            throw new InvalidDataException("It is no sound of a content build.");
        }

        return header[4] == Version
            ? header[5]
            : throw new InvalidDataException($"The sound is of version {header[4]}, not {Version}; import its source again.");
    }

    // The file after its header.
    private sealed class Storage(ISoundStorage storage) : ISoundStorage
    {
        public Stream Open()
        {
            var stream = storage.Open();
            try
            {
                ReadHeader(stream);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
    }
}
