using System.Buffers.Binary;

namespace Adamantium.Pulsar.Decoding;

/// <summary>WAV files: samples of 8, 16, 24 or 32 bits, or floats of 32 or 64, and the loop where one is written.</summary>
internal sealed class WavDecoder : ISoundDecoder
{
    private const int Pcm = 1;
    private const int Float = 3;
    private const int Extensible = 0xFFFE;
    private const int FormatLength = 26;
    private const int SamplerLength = 60;
    private const int BufferFrames = 4096;

    private readonly Stream _stream;
    private readonly byte[] _buffer;
    private readonly long _start;
    private readonly int _sampleBytes;
    private readonly bool _isFloat;
    private long _frame;

    public WavDecoder(Stream stream)
    {
        _stream = stream;
        Span<byte> riff = stackalloc byte[12];
        if (stream.ReadAtLeast(riff, riff.Length, throwOnEndOfStream: false) < riff.Length ||
            !riff.StartsWith("RIFF"u8) || !riff[8..].SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("It is no WAV file.");
        }

        Span<byte> format = stackalloc byte[FormatLength];
        Span<byte> sampler = stackalloc byte[SamplerLength];
        var formatLength = 0;
        var samplerLength = 0;
        long dataLength = 0;
        Span<byte> header = stackalloc byte[8];
        while (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length)
        {
            var at = stream.Position;
            // A file cut off, or written without knowing its length, ends where it ends.
            var length = Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(header[4..]), stream.Length - at);
            if (header.StartsWith("fmt "u8))
            {
                formatLength = ReadChunk(stream, at, length, format);
            }
            else if (header.StartsWith("smpl"u8))
            {
                samplerLength = ReadChunk(stream, at, length, sampler);
            }
            else if (header.StartsWith("data"u8) && dataLength == 0)
            {
                _start = at;
                dataLength = length;
            }

            var next = at + length + (length & 1);
            if (next >= stream.Length)
            {
                break;
            }

            stream.Position = next;
        }

        (Format, _sampleBytes, _isFloat) = ReadFormat(format[..formatLength]);
        Frames = dataLength / (_sampleBytes * Format.Channels);
        if (Frames == 0)
        {
            throw new InvalidDataException("The WAV file has no samples.");
        }

        Loop = LoopOf(sampler[..samplerLength], Frames);
        _buffer = new byte[BufferFrames * _sampleBytes * Format.Channels];
        stream.Position = _start;
    }

    public AudioFormat Format { get; }

    public long Frames { get; }

    public SoundLoop? Loop { get; }

    public int Read(Span<float> samples)
    {
        var frameBytes = _sampleBytes * Format.Channels;
        var done = 0;
        var wanted = (int)Math.Min(samples.Length / Format.Channels, Frames - _frame);
        while (done < wanted)
        {
            var frames = Math.Min(wanted - done, BufferFrames);
            var bytes = _buffer.AsSpan(0, frames * frameBytes);
            frames = _stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) / frameBytes;
            if (frames == 0)
            {
                throw new InvalidDataException("The WAV file ended before its samples did.");
            }

            for (var sample = 0; sample < frames * Format.Channels; sample++)
            {
                samples[done * Format.Channels + sample] = SampleOf(bytes.Slice(sample * _sampleBytes, _sampleBytes), _isFloat);
            }

            done += frames;
            _frame += frames;
        }

        return done;
    }

    public void Seek(long frame)
    {
        if (frame < 0 || frame > Frames)
        {
            throw new InvalidDataException($"The WAV file has no frame {frame}.");
        }

        _stream.Position = _start + frame * _sampleBytes * Format.Channels;
        _frame = frame;
    }

    public void Dispose() => _stream.Dispose();

    private static int ReadChunk(Stream stream, long at, long length, Span<byte> chunk)
    {
        stream.Position = at;
        chunk = chunk[..(int)Math.Min(length, chunk.Length)];
        return stream.ReadAtLeast(chunk, chunk.Length, throwOnEndOfStream: false);
    }

    private static (AudioFormat Format, int SampleBytes, bool IsFloat) ReadFormat(ReadOnlySpan<byte> format)
    {
        if (format.Length < 16)
        {
            throw new InvalidDataException("The WAV file does not say its format.");
        }

        int tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(format[2..]);
        var rate = BinaryPrimitives.ReadUInt32LittleEndian(format[4..]);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(format[14..]);
        var speakers = Speakers.None;
        if (tag == Extensible && format.Length == FormatLength)
        {
            tag = BinaryPrimitives.ReadUInt16LittleEndian(format[24..]);
            // Speakers fewer or more than the channels, or none, are not said.
            speakers = (Speakers)BinaryPrimitives.ReadUInt32LittleEndian(format[20..]);
            speakers = AudioFormat.Fits(speakers, channels) ? speakers : Speakers.None;
        }

        if (channels is < 1 or > AudioFormat.MostChannels)
        {
            throw new InvalidDataException($"The WAV file has {channels} channels, not from one to {AudioFormat.MostChannels}.");
        }

        if (rate is 0 or > AudioFormat.HighestRate)
        {
            throw new InvalidDataException($"The WAV file has a rate of {rate} Hz.");
        }

        // A sample may be in a wider container, its upper bits.
        var bytes = BinaryPrimitives.ReadUInt16LittleEndian(format[12..]) / channels;
        var isWhole = tag == Pcm && bytes is >= 1 and <= 4 && bits >= 1 && bits <= bytes * 8;
        var isFloat = tag == Float && bytes is 4 or 8 && bits == bytes * 8;
        if (!isWhole && !isFloat)
        {
            throw new InvalidDataException($"The WAV file has samples of an unknown kind ({tag}, {bits} bits in {bytes} bytes).");
        }

        return (new AudioFormat((int)rate, channels, speakers), bytes, isFloat);
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
    private static SoundLoop? LoopOf(ReadOnlySpan<byte> sampler, long frames)
    {
        if (sampler.Length < SamplerLength || BinaryPrimitives.ReadUInt32LittleEndian(sampler[28..]) == 0)
        {
            return null;
        }

        var start = BinaryPrimitives.ReadUInt32LittleEndian(sampler[44..]);
        var end = BinaryPrimitives.ReadUInt32LittleEndian(sampler[48..]) + 1L;
        return start < end && end <= frames && end <= int.MaxValue ? new SoundLoop((int)start, (int)end) : null;
    }
}
