namespace Adamantium.Pulsar.Decoding;

/// <summary>An MP3 stream read as it comes - a station's: frame after frame, found by their headers, with no table of
/// them and no seeking. How long it is is not known. Bytes that are no frame - a tag, damage, the middle of a frame the
/// stream began in - are passed by to the next frame that the one after it agrees with.</summary>
internal sealed class Mp3StreamDecoder : ISoundDecoder
{
    private const int LongestFrame = 1441;
    // Past this many bytes of no frame, it is no MP3 stream, or no more of one.
    private const int LongestSearch = 1 << 16;
    // Of two channels: a station may change from frames of one to frames of two, and one is heard in both.
    private const int Channels = 2;

    private readonly ForwardReader _reader;
    private readonly Mp3FrameDecoder _frames = new();
    private readonly Mp3FrameHeader _first;
    private readonly byte[] _frame = new byte[LongestFrame];
    private readonly float[] _decoded;
    private int _decodedFrames;
    private int _decodedRead;

    /// <exception cref="InvalidDataException">No frames come.</exception>
    public Mp3StreamDecoder(ForwardReader reader)
    {
        _reader = reader;
        SkipTags();
        _first = NextHeader(null) ?? throw new InvalidDataException("It is no MP3 stream.");
        Format = new AudioFormat(_first.SampleRate, Channels);
        _decoded = new float[_first.Samples * Channels];
    }

    public AudioFormat Format { get; }

    public long Frames => -1;

    public SoundLoop? Loop => null;

    public string Codec => "MP3";

    public SoundTags Tags => default;

    public int Read(Span<float> samples)
    {
        var wanted = samples.Length / Channels;
        var done = 0;
        while (done < wanted)
        {
            if (_decodedRead == _decodedFrames)
            {
                if (!DecodeNext())
                {
                    break;
                }

                continue;
            }

            var taken = Math.Min(wanted - done, _decodedFrames - _decodedRead);
            _decoded.AsSpan(_decodedRead * Channels, taken * Channels).CopyTo(samples[(done * Channels)..]);
            _decodedRead += taken;
            done += taken;
        }

        return done;
    }

    public void Seek(long frame) =>
        throw new InvalidDataException("A stream that comes as it plays is not sought in.");

    public void Dispose() => _reader.Dispose();

    // ID3v2 tags a stream of a file may begin by.
    private void SkipTags()
    {
        while (_reader.Has(Id3Tags.HeaderLength) && Id3Tags.LengthOf(_reader.Ahead(Id3Tags.HeaderLength)) is >= 0 and var length)
        {
            _reader.Skip(length);
        }
    }

    // One frame into the decoded samples; false where no more come.
    private bool DecodeNext()
    {
        if (NextHeader(_first) is not { } header)
        {
            return false;
        }

        var length = header.FrameLength;
        _reader.Ahead(length).CopyTo(_frame);
        _reader.Take(length);
        _frames.Decode(header, _frame.AsSpan(0, length), _decoded, Channels);
        _decodedFrames = header.Samples;
        _decodedRead = 0;
        return true;
    }

    // The header of the next frame - like the first, where there is one - that the one after it agrees with, or that the
    // stream ends after; the bytes before it are passed by.
    private Mp3FrameHeader? NextHeader(Mp3FrameHeader? like)
    {
        for (var passed = 0; passed <= LongestSearch; passed++)
        {
            if (!_reader.Has(Mp3FrameHeader.Length))
            {
                return null;
            }

            if (Mp3FrameHeader.TryRead(_reader.Ahead(Mp3FrameHeader.Length), out var header) &&
                (like is not { } first || header.IsLike(first)) && IsFollowed(header))
            {
                return header;
            }

            _reader.Take(1);
        }

        return null;
    }

    private bool IsFollowed(Mp3FrameHeader header)
    {
        var length = header.FrameLength;
        if (!_reader.Has(length + Mp3FrameHeader.Length))
        {
            // The last frame of the stream is one too, whatever bytes are left after it.
            return _reader.Available >= length;
        }

        return Mp3FrameHeader.TryRead(_reader.Ahead(length + Mp3FrameHeader.Length)[length..], out var next) &&
            next.IsLike(header);
    }
}
