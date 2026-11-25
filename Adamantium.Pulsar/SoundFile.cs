using Adamantium.Pulsar.Decoding;

namespace Adamantium.Pulsar;

/// <summary>Sound files: WAV and FLAC, told apart by how they begin.</summary>
public static class SoundFile
{
    private const int FirstRead = 1 << 16;

    /// <summary>The decoder of the file at the stream's position, which it seeks in, and disposes with itself.</summary>
    /// <exception cref="InvalidDataException">It is no sound file, or one that can not be read; the stream is disposed.
    /// </exception>
    public static ISoundDecoder Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            var start = stream.Position;
            Span<byte> signature = stackalloc byte[4];
            var read = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
            stream.Position = start;
            if (read == signature.Length)
            {
                if (signature.SequenceEqual("RIFF"u8))
                {
                    return new WavDecoder(stream);
                }

                if (signature.SequenceEqual("fLaC"u8))
                {
                    return new FlacDecoder(stream);
                }
            }

            throw new InvalidDataException("It is no WAV or FLAC file.");
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>The whole of the file.</summary>
    /// <exception cref="InvalidDataException">It is no sound file, or one that can not be read.</exception>
    public static SoundData Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return Read(new MemoryStream(file, writable: false));
    }

    /// <summary>The whole of the file at the stream's position; the stream is disposed.</summary>
    /// <exception cref="InvalidDataException">It is no sound file, or one that can not be read.</exception>
    public static SoundData Read(Stream stream)
    {
        using var decoder = Open(stream);
        var channels = decoder.Format.Channels;
        // As long as it says it is, if that is what comes: a damaged one may say anything.
        var samples = new float[Math.Clamp(decoder.Frames, 1, FirstRead) * channels];
        var read = 0;
        while (true)
        {
            var got = decoder.Read(samples.AsSpan(read)) * channels;
            read += got;
            if (read < samples.Length)
            {
                break;
            }

            var wanted = decoder.Frames > 0 && decoder.Frames * channels > read
                ? Math.Min(decoder.Frames * channels, samples.Length * 2L)
                : samples.Length * 2L;
            Array.Resize(ref samples, (int)Math.Min(wanted, Array.MaxLength - 1));
        }

        Array.Resize(ref samples, read);
        return read == 0
            ? throw new InvalidDataException("The sound file has no samples.")
            : new SoundData(decoder.Format, samples, decoder.Loop);
    }
}
