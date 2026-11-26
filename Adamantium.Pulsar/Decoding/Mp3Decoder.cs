using System.Buffers.Binary;

namespace Adamantium.Pulsar.Decoding;

/// <summary>MP3 files: MPEG-1, 2 and 2.5 Layer III, after any ID3v2 tags. A Xing or Info frame says how many frames
/// there are, and the LAME tag in it the coder's delay and padding: those samples are not the sound's. Every frame is
/// found at the beginning, so a seek is to the sample.</summary>
internal sealed class Mp3Decoder : ISoundDecoder
{
    // What the synthesis filter and the transform delay every coder's sound by.
    private const int DecoderDelay = 529;
    private const int LongestFrame = 1441;
    // How far a stream's first frame is looked for, past its tags.
    private const int LongestSearch = 1 << 16;
    // A frame takes this much of the frames before: main_data_begin's most.
    private const int LongestReservoir = 511;
    // Of the lower rates', of 8 bits.
    private const int LongestLowerReservoir = 255;
    private const int VbriAt = Mp3FrameHeader.Length + 32;
    private const int Id3v1Length = 128;
    private const int ApeFooterLength = 32;
    private const int ReservoirCapacity = 4096;

    private readonly Stream _stream;
    private readonly Mp3Layer3 _layer3 = new();
    private readonly Mp3FrameHeader _first;
    private readonly long[] _frameAt;
    // Where the frames end: before the tags at the end of the file.
    private readonly long _end;
    private readonly int _startSkip;
    private readonly byte[] _frame = new byte[LongestFrame];
    private readonly byte[] _reservoir = new byte[ReservoirCapacity];
    private readonly float[] _decoded;
    private int _reservoirLength;
    // The frame of the file decoded next.
    private int _next;
    private int _decodedFrames;
    private int _decodedRead;
    // Of the sound, the next frame read; and how many decoded frames to drop before it.
    private long _position;
    private long _drop;

    public Mp3Decoder(Stream stream)
    {
        _stream = stream;
        var start = SkipTags(stream);
        _end = EndOfFrames(stream);
        var first = FindFrame(stream, start, start + LongestSearch) ??
            throw new InvalidDataException("It is no MP3 file.");
        _first = ReadHeaderAt(first);
        var (isInfo, delay, padding) = ReadInfoFrame(first);
        var frames = FindFrames(isInfo ? first + _first.FrameLength : first, out var channels);
        _frameAt = [.. frames];
        if (_frameAt.Length == 0)
        {
            throw new InvalidDataException("The MP3 file has no frames.");
        }

        // A stream of frames of one channel and of two is of two: the one heard in both.
        Format = new AudioFormat(_first.SampleRate, channels);
        var samples = (long)_frameAt.Length * _first.Samples;
        // Without the coder's word, all that was decoded is the sound, as other decoders have it.
        _startSkip = delay < 0 ? 0 : delay + DecoderDelay;
        var end = delay < 0 ? samples : Math.Min(samples, samples - padding + DecoderDelay);
        Frames = Math.Max(0, end - _startSkip);
        if (Frames == 0)
        {
            throw new InvalidDataException("The MP3 file has no samples.");
        }

        _decoded = new float[_first.Samples * channels];
        _drop = _startSkip;
    }

    public AudioFormat Format { get; }

    public long Frames { get; }

    public SoundLoop? Loop => null;

    public int Read(Span<float> samples)
    {
        var channels = Format.Channels;
        var wanted = (int)Math.Min(samples.Length / channels, Frames - _position);
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

            var dropped = (int)Math.Min(_drop, _decodedFrames - _decodedRead);
            if (dropped > 0)
            {
                _decodedRead += dropped;
                _drop -= dropped;
                continue;
            }

            var taken = Math.Min(wanted - done, _decodedFrames - _decodedRead);
            _decoded.AsSpan(_decodedRead * channels, taken * channels).CopyTo(samples[(done * channels)..]);
            _decodedRead += taken;
            done += taken;
        }

        _position += done;
        return done;
    }

    // Its frame is decoded from a few before, which fill the reservoir and the filters as reading on would have.
    public void Seek(long frame)
    {
        if (frame < 0 || frame > Frames)
        {
            throw new InvalidDataException($"The MP3 file has no frame {frame}.");
        }

        // The two granules before the one sought make what it is heard with: the frame before at MPEG-1, the two
        // before at the lower rates. The first of them takes main data from as far back as main_data_begin can say.
        var sample = frame + _startSkip;
        var target = (int)Math.Min(sample / _first.Samples, _frameAt.Length);
        var from = Math.Max(0, target - (_first.IsFirst ? 1 : 2));
        var reservoir = _first.IsFirst ? LongestReservoir : LongestLowerReservoir;
        for (var bytes = 0; from > 0 && bytes < reservoir; from--)
        {
            bytes += MainDataOf(from - 1);
        }

        _layer3.Reset();
        _reservoirLength = 0;
        _next = from;
        _decodedFrames = _decodedRead = 0;
        _drop = sample - (long)from * _first.Samples;
        _position = frame;
    }

    public void Dispose() => _stream.Dispose();

    // How many bytes of main data a frame holds.
    private int MainDataOf(int frame)
    {
        var header = ReadHeaderAt(_frameAt[frame]);
        return Math.Max(0, header.FrameLength - header.SideInfoAt - header.SideInfoLength);
    }

    // ID3v2 tags, one after another, each with its footer where it says.
    private static long SkipTags(Stream stream)
    {
        var at = stream.Position;
        Span<byte> tag = stackalloc byte[10];
        while (true)
        {
            stream.Position = at;
            if (stream.ReadAtLeast(tag, tag.Length, throwOnEndOfStream: false) < tag.Length || !tag.StartsWith("ID3"u8) ||
                ((tag[6] | tag[7] | tag[8] | tag[9]) & 0x80) != 0)
            {
                return at;
            }

            var length = tag[6] << 21 | tag[7] << 14 | tag[8] << 7 | tag[9];
            at += 10 + length + ((tag[5] & 0x10) != 0 ? 10 : 0);
        }
    }

    // An ID3v1 tag, its 128 bytes beginning by TAG; an APEv2 tag before it or at the end, by its footer, and its header
    // where the footer says there is one.
    private static long EndOfFrames(Stream stream)
    {
        var end = stream.Length;
        Span<byte> bytes = stackalloc byte[ApeFooterLength];
        if (end >= Id3v1Length && ReadAt(stream, end - Id3v1Length, bytes[..3]) && bytes[..3].SequenceEqual("TAG"u8))
        {
            end -= Id3v1Length;
        }

        if (end >= ApeFooterLength && ReadAt(stream, end - ApeFooterLength, bytes) && bytes.StartsWith("APETAGEX"u8))
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
            var hasHeader = (BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..]) & 0x80000000) != 0;
            end = Math.Max(0, end - length - (hasHeader ? ApeFooterLength : 0));
        }

        return end;
    }

    private static bool ReadAt(Stream stream, long at, Span<byte> bytes)
    {
        stream.Position = at;
        return stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) == bytes.Length;
    }

    // A frame header that the next one agrees with: a lone one may be bytes of a tag or a picture.
    private long? FindFrame(Stream stream, long from, long to)
    {
        var buffer = new byte[4096];
        for (var at = from; at < Math.Min(to, _end - Mp3FrameHeader.Length);)
        {
            stream.Position = at;
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            for (var index = 0; index + Mp3FrameHeader.Length <= read; index++)
            {
                if (Mp3FrameHeader.TryRead(buffer.AsSpan(index), out var header) && IsFollowed(at + index, header))
                {
                    return at + index;
                }
            }

            at += Math.Max(1, read - Mp3FrameHeader.Length + 1);
        }

        return null;
    }

    private bool IsFollowed(long at, Mp3FrameHeader header)
    {
        var next = at + header.FrameLength;
        // The last frame of a file is one too.
        return next == _end || next < _end && TryHeaderAt(next, out var following) && following.IsLike(header);
    }

    private Mp3FrameHeader ReadHeaderAt(long at) =>
        TryHeaderAt(at, out var header) ? header : throw new InvalidDataException("The MP3 file is damaged.");

    private bool TryHeaderAt(long at, out Mp3FrameHeader header)
    {
        Span<byte> bytes = stackalloc byte[Mp3FrameHeader.Length];
        _stream.Position = at;
        header = default;
        return _stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false) == bytes.Length &&
            Mp3FrameHeader.TryRead(bytes, out header);
    }

    // A frame of no sound that says of the others: Xing or Info, and the LAME tag in it. The delay is -1 where it is not
    // said.
    private (bool IsInfo, int Delay, int Padding) ReadInfoFrame(long at)
    {
        var length = _first.FrameLength;
        var frame = new byte[length];
        _stream.Position = at;
        if (_stream.ReadAtLeast(frame, length, throwOnEndOfStream: false) < length)
        {
            return (false, -1, 0);
        }

        var tagAt = _first.SideInfoAt + _first.SideInfoLength;
        var tag = frame.AsSpan(tagAt);
        // Fraunhofer's, 32 bytes after the header whatever the side information's length.
        if (tag.StartsWith("VBRI"u8) || frame.Length >= VbriAt + 4 && frame.AsSpan(VbriAt).StartsWith("VBRI"u8))
        {
            return (true, -1, 0);
        }

        if (tag.Length < 8 || !tag.StartsWith("Xing"u8) && !tag.StartsWith("Info"u8))
        {
            return (false, -1, 0);
        }

        var flags = BinaryPrimitives.ReadUInt32BigEndian(tag[4..]);
        var lameAt = 8 + ((flags & 1) != 0 ? 4 : 0) + ((flags & 2) != 0 ? 4 : 0) + ((flags & 4) != 0 ? 100 : 0) +
            ((flags & 8) != 0 ? 4 : 0);
        // The coder's name, then what LAME's tag holds: the delay and padding 21 bytes on, 12 bits each.
        if (tag.Length < lameAt + 24 || !IsName(tag.Slice(lameAt, 4)))
        {
            return (true, -1, 0);
        }

        var lame = tag[lameAt..];
        return (true, lame[21] << 4 | lame[22] >> 4, (lame[22] & 15) << 8 | lame[23]);
    }

    private static bool IsName(ReadOnlySpan<byte> name)
    {
        foreach (var letter in name)
        {
            if (letter is < (byte)'0' or > (byte)'z')
            {
                return false;
            }
        }

        return true;
    }

    // Where each frame is, to the end or the first bytes that are no frame of the stream's: a tag, or damage
    // past which another frame is looked for.
    private List<long> FindFrames(long at, out int channels)
    {
        var frames = new List<long>();
        channels = 1;
        while (at + Mp3FrameHeader.Length <= _end)
        {
            if (TryHeaderAt(at, out var header) && header.IsLike(_first))
            {
                if (at + header.FrameLength > _end)
                {
                    break;
                }

                frames.Add(at);
                channels = Math.Max(channels, header.Channels);
                at += header.FrameLength;
                continue;
            }

            if (FindFrame(_stream, at + 1, at + LongestSearch) is not { } next)
            {
                break;
            }

            at = next;
        }

        return frames;
    }

    // One frame into the decoded samples; false past the last.
    private bool DecodeNext()
    {
        if (_next >= _frameAt.Length)
        {
            return false;
        }

        var at = _frameAt[_next++];
        var header = ReadHeaderAt(at);
        var length = header.FrameLength;
        _stream.Position = at;
        _stream.ReadExactly(_frame.AsSpan(0, length));
        _decodedFrames = header.Samples;
        _decodedRead = 0;
        var decoded = _decoded.AsSpan(0, header.Samples * header.Channels);
        var sideEnd = header.SideInfoAt + header.SideInfoLength;
        if (!_layer3.ReadSideInfo(header, _frame.AsSpan(header.SideInfoAt, header.SideInfoLength), out var begin) ||
            sideEnd > length)
        {
            _decoded.AsSpan(0, header.Samples * Format.Channels).Clear();
            _reservoirLength = 0;
            return true;
        }

        // The main data begins that far back in the frames before; what is kept of them is no more than it takes.
        var mainData = _frame.AsSpan(sideEnd, length - sideEnd);
        var keep = Math.Min(_reservoirLength, LongestReservoir);
        _reservoir.AsSpan(_reservoirLength - keep, keep).CopyTo(_reservoir);
        _reservoirLength = keep;
        var hasMainData = begin <= _reservoirLength;
        var mainStart = _reservoirLength - begin;
        mainData.CopyTo(_reservoir.AsSpan(_reservoirLength));
        _reservoirLength += mainData.Length;
        if (hasMainData)
        {
            _layer3.Decode(header, _reservoir.AsSpan(mainStart, _reservoirLength - mainStart), decoded);
        }
        else
        {
            decoded.Clear();
        }

        // A frame of one channel in a stream of two: from the last sample back, each in both.
        if (header.Channels < Format.Channels)
        {
            _layer3.ShareFirstChannel();
            for (var sample = header.Samples - 1; sample >= 0; sample--)
            {
                _decoded[sample * 2 + 1] = _decoded[sample * 2] = _decoded[sample];
            }
        }

        return true;
    }
}
