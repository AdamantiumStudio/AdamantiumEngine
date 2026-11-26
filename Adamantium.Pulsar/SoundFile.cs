using Adamantium.Pulsar.Decoding;

namespace Adamantium.Pulsar;

/// <summary>Sound files: WAV, FLAC, MP3, and Ogg of Vorbis, Opus or FLAC, told apart by how they begin.</summary>
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

                if (signature.SequenceEqual("OggS"u8))
                {
                    return OggFile.Open(stream);
                }

                // Some begin by zeros before the first frame.
                if (signature.StartsWith("ID3"u8) || Mp3FrameHeader.TryRead(signature, out _) ||
                    signature.IndexOfAnyExcept((byte)0) < 0)
                {
                    return new Mp3Decoder(stream);
                }
            }

            throw new InvalidDataException("It is no WAV, FLAC, MP3 or Ogg file.");
        }
        catch (EndOfStreamException e)
        {
            stream.Dispose();
            throw new InvalidDataException("The sound file is cut short.", e);
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
        ArgumentNullException.ThrowIfNull(stream);
        using var decoder = Open(stream.CanSeek ? stream : CopyOf(stream));
        var channels = decoder.Format.Channels;
        var most = Math.Min(Sound.MostFrames, Array.MaxLength / channels);
        if (decoder.Frames > most)
        {
            throw TooLong();
        }

        // As long as it says it is, if that is what comes: a damaged one may say anything.
        var frames = (int)Math.Min(decoder.Frames < 0 ? FirstRead : decoder.Frames, FirstRead);
        var samples = new float[frames * channels];
        var read = 0;
        while (true)
        {
            read += decoder.Read(samples.AsSpan(read * channels));
            if (read < frames || read == decoder.Frames)
            {
                break;
            }

            if (frames == most)
            {
                throw TooLong();
            }

            frames = (int)Math.Min(frames * 2L, decoder.Frames < 0 ? most : decoder.Frames);
            Array.Resize(ref samples, frames * channels);
        }

        if (read == 0)
        {
            throw new InvalidDataException("The sound file has no samples.");
        }

        Array.Resize(ref samples, read * channels);
        // One of unknown length may say a loop past its end.
        return new SoundData(decoder.Format, samples, decoder.Loop is { } loop && loop.End <= read ? loop : null);
    }

    private static MemoryStream CopyOf(Stream stream)
    {
        using (stream)
        {
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            return copy;
        }
    }

    private static InvalidDataException TooLong() => new("The sound file is too long to be held in memory.");
}
