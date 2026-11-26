using System.Buffers.Binary;

namespace Adamantium.Pulsar.Decoding;

/// <summary>FLAC files, RFC 9639. Each frame's checksum is checked.</summary>
internal sealed class FlacDecoder : ISoundDecoder
{
    private const int StreamInfo = 0;
    private const int SeekTable = 3;
    private const int VorbisComment = 4;
    private const int StreamInfoLength = 34;
    private const int SeekPointLength = 18;
    // A comment block holds pictures too: one larger has no loop or speakers worth its reading.
    private const int LongestComments = 1 << 20;
    private const int LeftSide = 8;
    private const int SideRight = 9;
    private const int MidSide = 10;
    // A seek that gets this near reads on from there.
    private const long NearBytes = 1 << 16;

    private readonly Stream _stream;
    private readonly BitReader _bits;
    private readonly long _start;
    private readonly int _bitsPerSample;
    private readonly int _smallestBlock;
    private readonly int _largestBlock;
    private readonly List<SeekPoint> _seekPoints = [];
    // A side channel of 32 bits has 33.
    private readonly long[][] _channels;
    private readonly int[] _coefficients = new int[32];
    // Of the sound, the first frame decoded, how many are, and how many of them were read.
    private long _blockFrame;
    private int _blockSize;
    private int _blockRead;
    // Where the next frame decoded is in the sound, it says itself.
    private bool _isSought;

    public FlacDecoder(Stream stream)
    {
        _stream = stream;
        Span<byte> header = stackalloc byte[4];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < 4 || !header.SequenceEqual("fLaC"u8))
        {
            throw new InvalidDataException("It is no FLAC file.");
        }

        var hasInfo = false;
        for (var isLast = false; !isLast;)
        {
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                throw new InvalidDataException("The FLAC file is cut short.");
            }

            isLast = (header[0] & 0x80) != 0;
            var type = header[0] & 0x7F;
            var length = header[1] << 16 | header[2] << 8 | header[3];
            var next = stream.Position + length;
            if (next > stream.Length)
            {
                throw new InvalidDataException("The FLAC file is cut short.");
            }

            if (hasInfo != (type != StreamInfo))
            {
                throw new InvalidDataException("The FLAC file does not begin by what it holds.");
            }

            switch (type)
            {
                case StreamInfo:
                    (Format, _bitsPerSample, Frames, _smallestBlock, _largestBlock) = ReadInfo(length);
                    hasInfo = true;
                    break;
                case SeekTable:
                    ReadSeekTable(length);
                    break;
                case VorbisComment when length <= LongestComments:
                    Loop = ReadComments(length, out var speakers);
                    Format = AudioFormat.Fits(speakers, Format.Channels)
                        ? new AudioFormat(Format.SampleRate, Format.Channels, speakers)
                        : Format;
                    break;
            }

            stream.Position = next;
        }

        _start = stream.Position;
        // Their order and places are the file's word: those that can not be are no help.
        _seekPoints.RemoveAll(point => point.Offset > stream.Length - _start);
        _seekPoints.Sort((one, other) => one.Frame.CompareTo(other.Frame));
        _channels = new long[Format.Channels][];
        for (var channel = 0; channel < _channels.Length; channel++)
        {
            _channels[channel] = new long[_largestBlock];
        }

        _bits = new BitReader(stream);
        _bits.Seek(_start);
    }

    public AudioFormat Format { get; }

    public long Frames { get; }

    public SoundLoop? Loop { get; }

    public int Read(Span<float> samples)
    {
        var wanted = samples.Length / Format.Channels;
        var scale = 1f / (1L << _bitsPerSample - 1);
        var done = 0;
        while (done < wanted)
        {
            if (_blockRead == _blockSize && !DecodeFrame())
            {
                break;
            }

            var frames = Math.Min(wanted - done, _blockSize - _blockRead);
            for (var channel = 0; channel < _channels.Length; channel++)
            {
                var source = _channels[channel].AsSpan(_blockRead, frames);
                for (var frame = 0; frame < source.Length; frame++)
                {
                    samples[(done + frame) * _channels.Length + channel] = source[frame] * scale;
                }
            }

            done += frames;
            _blockRead += frames;
        }

        return done;
    }

    public void Seek(long frame)
    {
        if (frame < 0 || (Frames >= 0 && frame > Frames))
        {
            throw new InvalidDataException($"The FLAC file has no frame {frame}.");
        }

        if (frame < _blockFrame || frame > _blockFrame + _blockSize)
        {
            if (!TryDecodeAt(FindBefore(frame)) || _blockFrame > frame)
            {
                // What looked like a frame was none, or the table was wrong.
                TryDecodeAt(_start);
            }
        }

        while (frame > _blockFrame + _blockSize)
        {
            if (!DecodeFrame())
            {
                throw new InvalidDataException($"The FLAC file has no frame {frame}.");
            }
        }

        if (frame < _blockFrame)
        {
            throw new InvalidDataException($"The FLAC file has no frame {frame}.");
        }

        _blockRead = (int)(frame - _blockFrame);
    }

    public void Dispose() => _stream.Dispose();

    private (AudioFormat Format, int Bits, long Frames, int Smallest, int Largest) ReadInfo(int length)
    {
        Span<byte> info = stackalloc byte[StreamInfoLength];
        if (length < StreamInfoLength || _stream.ReadAtLeast(info, info.Length, throwOnEndOfStream: false) < info.Length)
        {
            throw new InvalidDataException("The FLAC file does not say its format.");
        }

        int smallest = BinaryPrimitives.ReadUInt16BigEndian(info);
        int largest = BinaryPrimitives.ReadUInt16BigEndian(info[2..]);
        var packed = BinaryPrimitives.ReadUInt64BigEndian(info[10..]);
        var rate = (int)(packed >> 44);
        var channels = (int)(packed >> 41 & 7) + 1;
        var bits = (int)(packed >> 36 & 31) + 1;
        var frames = (long)(packed & (1UL << 36) - 1);
        if (rate is 0 or > AudioFormat.HighestRate)
        {
            throw new InvalidDataException($"The FLAC file has a rate of {rate} Hz.");
        }

        if (bits < 4 || largest < 16 || smallest > largest)
        {
            throw new InvalidDataException($"The FLAC file is damaged: samples of {bits} bits, blocks of {smallest} to {largest}.");
        }

        return (new AudioFormat(rate, channels), bits, frames == 0 ? -1 : frames, smallest, largest);
    }

    private void ReadSeekTable(int length)
    {
        Span<byte> point = stackalloc byte[SeekPointLength];
        for (var left = length; left >= SeekPointLength; left -= SeekPointLength)
        {
            _stream.ReadExactly(point);
            var frame = BinaryPrimitives.ReadUInt64BigEndian(point);
            var offset = BinaryPrimitives.ReadUInt64BigEndian(point[8..]);
            // All ones is a place left for another.
            if (frame < long.MaxValue && offset < long.MaxValue)
            {
                _seekPoints.Add(new SeekPoint((long)frame, (long)offset));
            }
        }
    }

    // LOOPSTART, and LOOPLENGTH or LOOPEND where it is not the end of the sound: what game music is written with. The
    // speakers are said where they are not the usual.
    private SoundLoop? ReadComments(int length, out Speakers speakers)
    {
        var block = new byte[length];
        _stream.ReadExactly(block);
        var comments = VorbisComments.Read(block);
        speakers = comments.Speakers;
        return comments.LoopWithin(Frames);
    }

    // Where a frame begins that is not after the one sought: by the table, then halving what is between.
    private long FindBefore(long frame)
    {
        var low = _start;
        var high = _stream.Length;
        _bits.Seek(_start);
        if (_smallestBlock != _largestBlock && TryReadHeader(out var first) && first.IsCounted)
        {
            return _start;
        }

        foreach (var point in _seekPoints)
        {
            if (point.Frame > frame)
            {
                high = Math.Min(high, _start + point.Offset);
                break;
            }

            low = _start + point.Offset;
        }

        while (high - low > NearBytes)
        {
            var middle = low + (high - low) / 2;
            _bits.Seek(middle);
            if (TryFindHeader(high, out var at, out var header) && header.Frame <= frame)
            {
                low = at;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    // The next frame's, before a position of the stream.
    private bool TryFindHeader(long before, out long at, out FlacFrameHeader header)
    {
        while (_bits.Position < before && _bits.TryPeekByte(out var value))
        {
            _bits.Mark();
            at = _bits.Position;
            if (value == 0xFF)
            {
                if (TryReadHeader(out header))
                {
                    return true;
                }

                _bits.Rewind();
            }

            _bits.SkipByte();
        }

        at = 0;
        header = default;
        return false;
    }

    private bool TryReadHeader(out FlacFrameHeader header)
    {
        try
        {
            header = ReadHeader();
            return true;
        }
        catch (InvalidDataException)
        {
            header = default;
            return false;
        }
    }

    private FlacFrameHeader ReadHeader()
    {
        _bits.Mark();
        var sync = _bits.Read(16);
        if ((sync & 0xFFFE) != 0xFFF8)
        {
            throw Damaged("a frame does not begin as one");
        }

        var blockCode = (int)_bits.Read(4);
        var rateCode = (int)_bits.Read(4);
        var channels = (int)_bits.Read(4);
        var bitsCode = (int)_bits.Read(3);
        var reserved = _bits.Read(1);
        var number = ReadNumber();
        var blockSize = blockCode switch
        {
            0 => 0,
            1 => 192,
            <= 5 => 144 << blockCode,
            6 => (int)_bits.Read(8) + 1,
            7 => (int)_bits.Read(16) + 1,
            _ => 1 << blockCode,
        };
        // Its rate is the file's: only what it takes to write it is skipped.
        _bits.Read(rateCode switch { 12 => 8, 13 or 14 => 16, _ => 0 });
        var bits = bitsCode switch { 0 => _bitsPerSample, 1 => 8, 2 => 12, 4 => 16, 5 => 20, 6 => 24, 7 => 32, _ => 0 };
        var sum = FlacChecksums.OfHeader(_bits.Marked);
        if (sum != _bits.Read(8))
        {
            throw Damaged("a frame's beginning has another checksum");
        }

        var isArranged = channels < 8 ? channels + 1 == Format.Channels : channels <= MidSide && Format.Channels == 2;
        if (reserved != 0 || rateCode == 15 || blockSize == 0 || blockSize > _largestBlock || bits != _bitsPerSample ||
            !isArranged)
        {
            throw Damaged("a frame is of another kind than the file");
        }

        var isCounted = (sync & 1) == 0;
        return new FlacFrameHeader(isCounted ? number * _smallestBlock : number, blockSize, channels, bits, isCounted);
    }

    // As UTF-8 writes one, up to 36 bits.
    private long ReadNumber()
    {
        var first = _bits.Read(8);
        if (first < 0x80)
        {
            return first;
        }

        var length = 0;
        while (length < 8 && (first & 0x80 >> length) != 0)
        {
            length++;
        }

        if (length is < 2 or > 7)
        {
            throw Damaged("a frame's number is none");
        }

        long number = first & (1u << 7 - length) - 1;
        for (var index = 1; index < length; index++)
        {
            var next = _bits.Read(8);
            if ((next & 0xC0) != 0x80)
            {
                throw Damaged("a frame's number is none");
            }

            number = number << 6 | next & 0x3F;
        }

        return number;
    }

    private bool TryDecodeAt(long position)
    {
        _bits.Seek(position);
        // Read on, frames follow each other, whatever they say: some old files number those of many sizes.
        _isSought = position != _start;
        _blockFrame = 0;
        _blockSize = _blockRead = 0;
        try
        {
            return DecodeFrame();
        }
        catch (InvalidDataException) when (position != _start)
        {
            return false;
        }
    }

    // False at the end of the file.
    private bool DecodeFrame()
    {
        _blockFrame += _blockSize;
        _blockSize = _blockRead = 0;
        if (Frames >= 0 && _blockFrame >= Frames)
        {
            return false;
        }

        if (!_bits.TryPeekByte(out _))
        {
            return Frames < 0 ? false : throw new InvalidDataException("The FLAC file ended before its samples did.");
        }

        _bits.Mark();
        var header = ReadHeader();
        for (var channel = 0; channel < _channels.Length; channel++)
        {
            var isSide = header.Channels is LeftSide or MidSide ? channel == 1 : header.Channels == SideRight && channel == 0;
            ReadSubframe(header, _channels[channel], isSide);
        }

        _bits.SkipToByte();
        var sum = FlacChecksums.OfFrame(_bits.Marked);
        if (sum != _bits.Read(16))
        {
            throw Damaged("a frame has another checksum");
        }

        Arrange(header);
        if (_isSought)
        {
            _blockFrame = header.Frame;
            _isSought = false;
        }

        // The last frame of a file may be filled up past its end.
        _blockSize = Frames >= 0 ? (int)Math.Clamp(Frames - _blockFrame, 0, header.BlockSize) : header.BlockSize;
        return _blockSize != 0;
    }

    // The side channel has a bit more.
    private void ReadSubframe(in FlacFrameHeader header, long[] target, bool isSide)
    {
        var bits = header.BitsPerSample + (isSide ? 1 : 0);
        if (_bits.Read(1) != 0)
        {
            throw Damaged("a channel begins by no zero");
        }

        var type = (int)_bits.Read(6);
        var wasted = _bits.Read(1) != 0 ? _bits.ReadUnary() + 1 : 0;
        bits -= wasted;
        if (bits < 1)
        {
            throw Damaged("a channel has no bits");
        }

        var samples = target.AsSpan(0, header.BlockSize);
        switch (type)
        {
            case 0:
                samples.Fill(_bits.ReadSigned(bits));
                break;
            case 1:
                ReadWarmUp(samples, bits);
                break;
            case >= 8 and <= 12:
                ReadFixed(samples, type - 8, bits);
                break;
            case >= 32:
                ReadPredicted(samples, type - 31, bits);
                break;
            default:
                throw Damaged($"a channel is of an unknown kind ({type})");
        }

        if (wasted != 0)
        {
            foreach (ref var sample in samples)
            {
                sample <<= wasted;
            }
        }
    }

    private void ReadWarmUp(Span<long> samples, int bits)
    {
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = _bits.ReadSigned(bits);
        }
    }

    private void ReadFixed(Span<long> samples, int order, int bits)
    {
        CheckOrder(samples, order);
        ReadWarmUp(samples[..order], bits);
        ReadResidual(samples, order);
        switch (order)
        {
            case 1:
                for (var index = 1; index < samples.Length; index++)
                {
                    samples[index] += samples[index - 1];
                }

                break;
            case 2:
                for (var index = 2; index < samples.Length; index++)
                {
                    samples[index] += 2 * samples[index - 1] - samples[index - 2];
                }

                break;
            case 3:
                for (var index = 3; index < samples.Length; index++)
                {
                    samples[index] += 3 * (samples[index - 1] - samples[index - 2]) + samples[index - 3];
                }

                break;
            case 4:
                for (var index = 4; index < samples.Length; index++)
                {
                    samples[index] += 4 * (samples[index - 1] + samples[index - 3]) - 6 * samples[index - 2] - samples[index - 4];
                }

                break;
        }
    }

    private void ReadPredicted(Span<long> samples, int order, int bits)
    {
        CheckOrder(samples, order);
        ReadWarmUp(samples[..order], bits);
        var precision = (int)_bits.Read(4) + 1;
        var shift = (int)_bits.ReadSigned(5);
        if (precision == 16 || shift < 0)
        {
            throw Damaged("a channel's prediction is of no kind");
        }

        // The last one written is for the sample furthest back.
        var coefficients = _coefficients.AsSpan(0, order);
        for (var index = order - 1; index >= 0; index--)
        {
            coefficients[index] = (int)_bits.ReadSigned(precision);
        }

        ReadResidual(samples, order);
        for (var index = order; index < samples.Length; index++)
        {
            var past = samples.Slice(index - order, order);
            long sum = 0;
            for (var term = 0; term < past.Length; term++)
            {
                sum += past[term] * coefficients[term];
            }

            samples[index] += sum >> shift;
        }
    }

    private static void CheckOrder(Span<long> samples, int order)
    {
        if (order > samples.Length)
        {
            throw Damaged("a channel is shorter than what it begins by");
        }
    }

    private void ReadResidual(Span<long> samples, int order)
    {
        var method = (int)_bits.Read(2);
        if (method > 1)
        {
            throw Damaged("a channel's rest is coded in an unknown way");
        }

        var parameterBits = method == 0 ? 4 : 5;
        var escape = (1 << parameterBits) - 1;
        var partitionOrder = (int)_bits.Read(4);
        var partition = samples.Length >> partitionOrder;
        if (partition << partitionOrder != samples.Length || partition < order)
        {
            throw Damaged("a channel's rest is in parts that do not make it up");
        }

        for (var index = 0; index < 1 << partitionOrder; index++)
        {
            var part = samples.Slice(index * partition, partition);
            if (index == 0)
            {
                part = part[order..];
            }

            var parameter = (int)_bits.Read(parameterBits);
            if (parameter == escape)
            {
                ReadWarmUp(part, (int)_bits.Read(5));
                continue;
            }

            for (var sample = 0; sample < part.Length; sample++)
            {
                var folded = (long)_bits.ReadUnary() << parameter | _bits.Read(parameter);
                part[sample] = folded >> 1 ^ -(folded & 1);
            }
        }
    }

    private void Arrange(in FlacFrameHeader header)
    {
        if (header.Channels < LeftSide)
        {
            return;
        }

        var first = _channels[0].AsSpan(0, header.BlockSize);
        var second = _channels[1].AsSpan(0, header.BlockSize);
        for (var index = 0; index < first.Length; index++)
        {
            switch (header.Channels)
            {
                case LeftSide:
                    second[index] = first[index] - second[index];
                    break;
                case SideRight:
                    first[index] += second[index];
                    break;
                default:
                    var side = second[index];
                    var middle = first[index] << 1 | side & 1;
                    first[index] = middle + side >> 1;
                    second[index] = middle - side >> 1;
                    break;
            }
        }
    }

    private static InvalidDataException Damaged(string how) => new($"The FLAC file is damaged: {how}.");

    private readonly record struct SeekPoint(long Frame, long Offset);
}
