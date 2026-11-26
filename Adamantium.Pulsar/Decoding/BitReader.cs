using System.Buffers.Binary;
using System.Numerics;

namespace Adamantium.Pulsar.Decoding;

/// <summary>Bits of a stream, the highest of a byte first. What was read since <see cref="Mark"/> can be read again.
/// </summary>
internal sealed class BitReader(Stream stream)
{
    // More than any frame of a file that is not damaged.
    private const int MostKept = 1 << 24;

    private byte[] _buffer = new byte[1 << 16];
    // Of the buffer's first byte in the stream.
    private long _bufferAt;
    private int _length;
    private int _mark;
    // Of the next bit in the buffer.
    private long _bit;

    /// <summary>Of the next byte, where a whole number of them was read.</summary>
    public long Position => _bufferAt + (_bit >> 3);

    /// <summary>The bytes read since <see cref="Mark"/>.</summary>
    public ReadOnlySpan<byte> Marked => _buffer.AsSpan(_mark, (int)(_bit >> 3) - _mark);

    public void Mark() => _mark = (int)(_bit >> 3);

    /// <summary>Back to <see cref="Mark"/>.</summary>
    public void Rewind() => _bit = (long)_mark << 3;

    public void Seek(long position)
    {
        stream.Position = position;
        _bufferAt = position;
        _length = 0;
        _mark = 0;
        _bit = 0;
    }

    public void SkipToByte() => _bit = _bit + 7 & ~7L;

    /// <summary>False at the end of the stream.</summary>
    public bool TryPeekByte(out byte value)
    {
        if (!Ensure(1))
        {
            value = 0;
            return false;
        }

        value = _buffer[_bit >> 3];
        return true;
    }

    public void SkipByte() => _bit += 8;

    /// <param name="count">32 at most.</param>
    public uint Read(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        if (!Ensure(8) && _bit + count > (long)_length << 3)
        {
            throw Ended();
        }

        var bits = Peek64();
        _bit += count;
        return (uint)(bits >> 64 - count);
    }

    /// <param name="count">33 at most.</param>
    public long ReadSigned(int count)
    {
        if (count == 0)
        {
            return 0;
        }

        if (count > 32)
        {
            var high = ReadSigned(count - 32);
            return high << 32 | Read(32);
        }

        return (long)((ulong)Read(count) << 64 - count) >> 64 - count;
    }

    /// <summary>How many zeros there are before a one; the one is read too.</summary>
    public int ReadUnary()
    {
        var zeros = 0;
        while (true)
        {
            Ensure(8);
            var left = ((long)_length << 3) - _bit;
            if (left <= 0)
            {
                throw Ended();
            }

            // Of the 64, those the first byte was read of already come as zeros at the end.
            var known = (int)Math.Min(left, 64 - (int)(_bit & 7));
            var run = BitOperations.LeadingZeroCount(Peek64());
            if (run < known)
            {
                _bit += run + 1;
                return zeros + run;
            }

            _bit += known;
            zeros += known;
        }
    }

    private static InvalidDataException Ended() => new("The sound file is cut short.");

    private ulong Peek64()
    {
        var at = (int)(_bit >> 3);
        ulong bits;
        if (at + 8 <= _length)
        {
            bits = BinaryPrimitives.ReadUInt64BigEndian(_buffer.AsSpan(at));
        }
        else
        {
            bits = 0;
            for (var index = at; index < _length; index++)
            {
                bits |= (ulong)_buffer[index] << 56 - (index - at) * 8;
            }
        }

        return bits << (int)(_bit & 7);
    }

    // So many bytes from the next bit's on are in the buffer.
    private bool Ensure(int bytes)
    {
        if ((_bit >> 3) + bytes <= _length)
        {
            return true;
        }

        if (_mark > _buffer.Length / 2)
        {
            _buffer.AsSpan(_mark, _length - _mark).CopyTo(_buffer);
            _bufferAt += _mark;
            _length -= _mark;
            _bit -= (long)_mark << 3;
            _mark = 0;
        }
        else if (_buffer.Length - _length < bytes)
        {
            if (_buffer.Length == MostKept)
            {
                throw new InvalidDataException("The sound file is damaged: a frame of it has no end.");
            }

            Array.Resize(ref _buffer, _buffer.Length * 2);
        }

        _length += stream.ReadAtLeast(_buffer.AsSpan(_length), bytes, throwOnEndOfStream: false);
        return (_bit >> 3) + bytes <= _length;
    }
}
