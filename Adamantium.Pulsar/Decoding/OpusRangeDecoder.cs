namespace Adamantium.Pulsar.Decoding;

/// <summary>The range decoder of an Opus frame (RFC 6716, 4.1): symbols from its beginning, the highest bit first, and
/// raw bits from its end, the lowest first; past either end, zeros. Exact in every step, as what it reads is.</summary>
internal ref struct OpusRangeDecoder
{
    private const int SymbolBits = 8;
    private const int CodeBits = 32;
    private const uint CodeTop = 1u << (CodeBits - 1);
    private const uint CodeBottom = CodeTop >> SymbolBits;
    // The bits of the first byte past the range's 7.
    private const int CodeExtra = (CodeBits - 2) % SymbolBits + 1;
    private const int WindowBits = 32;

    private ReadOnlySpan<byte> _bytes;
    private int _offset;
    private int _endOffset;
    private uint _endWindow;
    private int _endBits;
    private int _totalBits;
    private uint _range;
    private uint _value;
    private uint _step;
    private int _remainder;

    public OpusRangeDecoder(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
        _totalBits = CodeBits + 1 - (CodeBits - CodeExtra) / SymbolBits * SymbolBits;
        _range = 1u << CodeExtra;
        _remainder = ReadByte();
        _value = _range - 1 - (uint)(_remainder >> (SymbolBits - CodeExtra));
        Normalize();
    }

    /// <summary>The frame's length in bytes, as it reads it.</summary>
    public readonly int Length => _bytes.Length;

    /// <summary>The state the encoder ended in, the same where the frame is read as written.</summary>
    public readonly uint FinalRange => _range;

    /// <summary>4.1.2: the value fs of a context of <paramref name="total"/>, before its symbol is known.</summary>
    public uint Decode(uint total)
    {
        _step = _range / total;
        var value = _value / _step;
        return total - Math.Min(value + 1, total);
    }

    /// <summary>As <see cref="Decode"/> of a total of 2 to the <paramref name="bits"/>.</summary>
    public uint DecodeBinary(int bits)
    {
        _step = _range >> bits;
        var value = _value / _step;
        return (1u << bits) - Math.Min(value + 1, 1u << bits);
    }

    /// <summary>The symbol of <see cref="Decode"/>: from <paramref name="low"/> up to <paramref name="high"/> of
    /// <paramref name="total"/>.</summary>
    public void Update(uint low, uint high, uint total)
    {
        var below = _step * (total - high);
        _value -= below;
        _range = low > 0 ? _step * (high - low) : _range - below;
        Normalize();
    }

    /// <summary>A bit whose chance of being 1 is 1 in 2 to the <paramref name="logChance"/>.</summary>
    public bool DecodeBit(int logChance)
    {
        var range = _range;
        var value = _value;
        var split = range >> logChance;
        var isOne = value < split;
        if (!isOne)
        {
            _value = value - split;
        }

        _range = isOne ? split : range - split;
        Normalize();
        return isOne;
    }

    /// <summary>A symbol of a table of the inverse cumulative frequencies, of a total of 2 to the
    /// <paramref name="bits"/>; the table ends by 0.</summary>
    public int DecodeInverse(scoped ReadOnlySpan<byte> inverse, int bits)
    {
        var top = _range;
        var value = _value;
        var unit = top >> bits;
        var symbol = -1;
        uint high;
        uint low = top;
        do
        {
            high = low;
            low = unit * inverse[++symbol];
        }
        while (value < low);

        _value = value - low;
        _range = high - low;
        Normalize();
        return symbol;
    }

    /// <summary>4.1.5: one of <paramref name="count"/> values alike, more than 1; one past the last, which only damage
    /// gives, is the last, as the reference has it.</summary>
    public uint DecodeUniform(uint count)
    {
        var top = count - 1;
        var bits = 32 - int.CreateTruncating(uint.LeadingZeroCount(top));
        if (bits <= 8)
        {
            var value = Decode(count);
            Update(value, value + 1, count);
            return value;
        }

        var low = bits - 8;
        var total = (top >> low) + 1;
        var high = Decode(total);
        Update(high, high + 1, total);
        var result = high << low | ReadBits(low);
        return Math.Min(result, top);
    }

    /// <summary>4.1.4: up to 25 raw bits from the end, the first the lowest.</summary>
    public uint ReadBits(int count)
    {
        var window = _endWindow;
        var available = _endBits;
        if (available < count)
        {
            do
            {
                window |= (uint)ReadByteFromEnd() << available;
                available += SymbolBits;
            }
            while (available <= WindowBits - SymbolBits);
        }

        var result = window & ((1u << count) - 1);
        _endWindow = window >> count;
        _endBits = available - count;
        _totalBits += count;
        return result;
    }

    /// <summary>The frame shorter by its last <paramref name="count"/> bytes, which a redundant frame takes: the raw
    /// bits and the budget end before them.</summary>
    public void Shrink(int count) => _bytes = _bytes[..(_bytes.Length - count)];

    /// <summary>The frame's bits all told as read, as a silent CELT frame has them.</summary>
    public void MarkAllRead() => _totalBits += _bytes.Length * 8 - Tell();

    /// <summary>4.1.6.1: the whole bits read, at most.</summary>
    public readonly int Tell() => _totalBits - Log(_range);

    /// <summary>4.1.6.2: in eighths of a bit.</summary>
    public readonly int TellFraction()
    {
        var log = Log(_range);
        var fraction = _range >> (log - 16);
        for (var step = 0; step < 3; step++)
        {
            fraction = fraction * fraction >> 15;
            var bit = (int)(fraction >> 16);
            log = log << 1 | bit;
            fraction >>= bit;
        }

        return _totalBits * 8 - log;
    }

    private static int Log(uint value) => 32 - int.CreateTruncating(uint.LeadingZeroCount(value));

    private int ReadByte() => _offset < _bytes.Length ? _bytes[_offset++] : 0;

    private int ReadByteFromEnd() => _endOffset < _bytes.Length ? _bytes[_bytes.Length - ++_endOffset] : 0;

    // 4.1.2.1.
    private void Normalize()
    {
        while (_range <= CodeBottom)
        {
            _totalBits += SymbolBits;
            _range <<= SymbolBits;
            var symbol = _remainder;
            _remainder = ReadByte();
            symbol = (symbol << SymbolBits | _remainder) >> (SymbolBits - CodeExtra);
            _value = ((_value << SymbolBits) + (uint)(0xFF & ~symbol)) & (CodeTop - 1);
        }
    }
}
