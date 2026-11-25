using System.Buffers.Binary;

namespace Adamantium.Pulsar;

/// <summary>WAV files: samples of 8, 16, 24 or 32 bits, or floats of 32 or 64, and the loop where one is written.</summary>
public static class WavFile
{
    private const int Pcm = 1;
    private const int Float = 3;
    private const int Extensible = 0xFFFE;

    /// <exception cref="InvalidDataException">It is no WAV file, or one that can not be read.</exception>
    public static SoundData Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Read(copy.GetBuffer().AsSpan(0, (int)copy.Length));
    }

    /// <exception cref="InvalidDataException">It is no WAV file, or one that can not be read.</exception>
    public static SoundData Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 12 || !file.StartsWith("RIFF"u8) || !file[8..].StartsWith("WAVE"u8))
        {
            throw new InvalidDataException("It is no WAV file.");
        }

        ReadOnlySpan<byte> format = default;
        ReadOnlySpan<byte> data = default;
        ReadOnlySpan<byte> sampler = default;
        for (var rest = file[12..]; rest.Length >= 8;)
        {
            var name = rest[..4];
            var size = BinaryPrimitives.ReadUInt32LittleEndian(rest[4..]);
            // A file cut off, or written without knowing its length, ends where it ends.
            var chunk = rest.Slice(8, (int)Math.Min(size, rest.Length - 8));
            if (name.SequenceEqual("fmt "u8))
            {
                format = chunk;
            }
            else if (name.SequenceEqual("data"u8) && data.IsEmpty)
            {
                data = chunk;
            }
            else if (name.SequenceEqual("smpl"u8))
            {
                sampler = chunk;
            }

            rest = rest[Math.Min(rest.Length, 8 + chunk.Length + (chunk.Length & 1))..];
        }

        return Read(format, data, sampler);
    }

    private static SoundData Read(ReadOnlySpan<byte> format, ReadOnlySpan<byte> data, ReadOnlySpan<byte> sampler)
    {
        if (format.Length < 16)
        {
            throw new InvalidDataException("The WAV file does not say its format.");
        }

        int tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
        var rate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
        if (tag == Extensible && format.Length >= 26)
        {
            tag = BinaryPrimitives.ReadUInt16LittleEndian(format[24..]);
        }

        if (channels is not (1 or 2))
        {
            throw new InvalidDataException($"The WAV file has {channels} channels, not one or two.");
        }

        if (rate is 0 or > 1_000_000)
        {
            throw new InvalidDataException($"The WAV file has a rate of {rate} Hz.");
        }

        if (!(tag == Pcm && bits is 8 or 16 or 24 or 32) && !(tag == Float && bits is 32 or 64))
        {
            throw new InvalidDataException($"The WAV file has samples of an unknown kind ({tag}, {bits} bits).");
        }

        var bytes = bits / 8;
        var frames = data.Length / (bytes * channels);
        if (frames == 0)
        {
            throw new InvalidDataException("The WAV file has no samples.");
        }

        var samples = new float[frames * channels];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = SampleOf(data.Slice(index * bytes, bytes), tag == Float);
        }

        return new SoundData(new AudioFormat((int)rate, channels), samples, LoopOf(sampler, frames));
    }

    private static float SampleOf(ReadOnlySpan<byte> bytes, bool isFloat) => (bytes.Length, isFloat) switch
    {
        (1, _) => (bytes[0] - 128) / 128f,
        (2, _) => BinaryPrimitives.ReadInt16LittleEndian(bytes) / 32768f,
        (3, _) => ((bytes[0] << 8 | bytes[1] << 16 | bytes[2] << 24) >> 8) / 8388608f,
        (4, false) => BinaryPrimitives.ReadInt32LittleEndian(bytes) / 2147483648f,
        (4, true) => FiniteOf(BinaryPrimitives.ReadSingleLittleEndian(bytes)),
        _ => FiniteOf((float)BinaryPrimitives.ReadDoubleLittleEndian(bytes)),
    };

    // One that is no number would spoil all it is mixed with.
    private static float FiniteOf(float sample) => float.IsFinite(sample) ? sample : 0;

    // The first one; the end it gives is the last frame played.
    private static SoundLoop? LoopOf(ReadOnlySpan<byte> sampler, int frames)
    {
        if (sampler.Length < 60 || BinaryPrimitives.ReadUInt32LittleEndian(sampler[28..]) == 0)
        {
            return null;
        }

        var start = BinaryPrimitives.ReadUInt32LittleEndian(sampler[44..]);
        var end = BinaryPrimitives.ReadUInt32LittleEndian(sampler[48..]) + 1L;
        return start < end && end <= frames ? new SoundLoop((int)start, (int)end) : null;
    }
}
