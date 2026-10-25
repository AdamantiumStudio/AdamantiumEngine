using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace Adamantium.Imaging.Jpeg;

/// <summary>The bits of a JPEG entropy-coded segment (ITU-T T.81, F.1.2.3), most significant first: a stuffed 0xFF 0x00
/// is the byte 0xFF, and a marker ends the segment - past it, and past the data, the bits read as zeros.</summary>
internal ref struct JpegBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;
    private ulong _bits;
    private int _count;
    private bool _ended;

    public JpegBitReader(ReadOnlySpan<byte> data, int position)
    {
        _data = data;
        _position = position;
    }

    /// <summary>Where the next unread byte is: at the marker that ended the segment, once one did.</summary>
    public int Position => _position;

    /// <summary>The next 64 bits, most significant first, without taking them; at least 32 are real data or the
    /// zeros past the end.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong Peek()
    {
        if (_count < 32)
        {
            Fill();
        }

        return _bits;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Skip(int bits)
    {
        _bits <<= bits;
        _count -= bits;
    }

    /// <summary>The next <paramref name="bits"/> bits (up to 16) as a number.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Bits(int bits)
    {
        if (bits == 0)
        {
            return 0;
        }

        var value = (int)(Peek() >> (64 - bits));
        Skip(bits);
        return value;
    }

    public bool Bit() => Bits(1) != 0;

    /// <summary>The next <paramref name="bits"/> bits as the signed value they code (F.2.2.1, EXTEND).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Receive(int bits)
    {
        if (bits == 0)
        {
            return 0;
        }

        var value = Bits(bits);
        return value < 1 << (bits - 1) ? value - (1 << bits) + 1 : value;
    }

    /// <summary>The symbol of the next code of <paramref name="table"/>; -1 when none matches.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Decode(JpegHuffman table)
    {
        var symbol = table.Decode(Peek(), out var length);
        if (length == 0)
        {
            return -1;
        }

        Skip(length);
        return symbol;
    }

    /// <summary>The symbol of the next code of <paramref name="table"/> and the signed value of the bits it says follow
    /// - its low four bits, as DC and AC symbols count them - taken together from one look at the bits: a code of up
    /// to 16 bits and a value of up to 15 fit the 32 a look holds. -1 when no code matches.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int DecodeValue(JpegHuffman table, out int value)
    {
        var bits = Peek();
        var symbol = table.Decode(bits, out var length);
        var size = symbol & 15;
        if (length == 0 || size == 0)
        {
            Skip(length);
            value = 0;
            return length == 0 ? -1 : symbol;
        }

        var raw = (int)((bits << length) >> (64 - size));
        Skip(length + size);
        value = raw < 1 << (size - 1) ? raw - (1 << size) + 1 : raw;
        return symbol;
    }

    /// <summary>Drops the bits left in the current byte and passes the restart marker that should follow; reading goes
    /// on after it. A restart marker missing - broken data - goes on at the next marker, or reads zeros.</summary>
    public void Restart()
    {
        _bits = 0;
        _count = 0;
        _ended = false;
        while (_position + 1 < _data.Length)
        {
            if (_data[_position] != 0xFF || _data[_position + 1] == 0x00)
            {
                _position++;
                continue;
            }

            if (_data[_position + 1] == 0xFF)
            {
                _position++;
                continue;
            }

            if (_data[_position + 1] is >= 0xD0 and <= 0xD7)
            {
                _position += 2;
            }

            return;
        }

        _position = _data.Length;
    }

    private void Fill()
    {
        // As many whole bytes as fit, at once, when none of them is 0xFF; byte by byte around one.
        if (!_ended && _position + 8 <= _data.Length)
        {
            var take = (64 - _count) >> 3;
            var word = BinaryPrimitives.ReadUInt64BigEndian(_data[_position..]);
            var bytes = take == 8 ? word : word >> (64 - take * 8);
            if (!HasFf(bytes))
            {
                _bits |= bytes << (64 - _count - take * 8);
                _count += take * 8;
                _position += take;
                return;
            }
        }

        while (_count <= 56)
        {
            byte value = 0;
            if (!_ended && _position < _data.Length)
            {
                value = _data[_position];
                if (value != 0xFF)
                {
                    _position++;
                }
                else if (_position + 1 < _data.Length && _data[_position + 1] == 0x00)
                {
                    _position += 2;
                }
                else
                {
                    _ended = true;
                    value = 0;
                }
            }
            else
            {
                _ended = true;
            }

            _bits |= (ulong)value << (56 - _count);
            _count += 8;
        }
    }

    private static bool HasFf(ulong word)
    {
        var inverted = ~word;
        return ((inverted - 0x0101010101010101UL) & ~inverted & 0x8080808080808080UL) != 0;
    }
}
