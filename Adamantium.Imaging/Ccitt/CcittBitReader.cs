using System;

namespace Adamantium.Imaging.Ccitt;

/// <summary>Reads bits most significant first; past the end of the data it reads zeros and says so.</summary>
internal ref struct CcittBitReader
{
    private readonly ReadOnlySpan<byte> _input;
    private long _bit;

    public CcittBitReader(ReadOnlySpan<byte> input)
    {
        _input = input;
    }

    /// <summary>The next bit to take, counted from the start of the data.</summary>
    public long Position
    {
        get => _bit;
        set => _bit = value;
    }

    /// <summary>Whether every bit of the data has been taken.</summary>
    public bool IsAtEnd => _bit >= (long)_input.Length * 8;

    /// <summary>Whether more bits were taken than the data has.</summary>
    public bool IsOverrun => _bit > (long)_input.Length * 8;

    /// <summary>The next <paramref name="count"/> bits, up to 24, without taking them.</summary>
    public int Peek(int count)
    {
        var at = _bit >> 3;
        var window = 0;
        for (var i = 0; i < 4; i++)
        {
            window = (window << 8) | (at + i < _input.Length ? _input[(int)(at + i)] : 0);
        }

        return (int)(((uint)window << (int)(_bit & 7)) >> (32 - count));
    }

    public void Skip(int count) => _bit += count;

    /// <summary>The next <paramref name="count"/> bits, or -1 when they run past the end of the data.</summary>
    public int Read(int count)
    {
        if (_bit + count > (long)_input.Length * 8)
        {
            _bit += count;
            return -1;
        }

        var value = Peek(count);
        _bit += count;
        return value;
    }

    public void AlignToByte() => _bit = (_bit + 7) & ~7L;

    /// <summary>The bytes the bits taken so far reach into, no more than <paramref name="length"/>.</summary>
    public int BytesTaken(int length) => (int)Math.Min(length, (_bit + 7) >> 3);

    /// <summary>Takes an end-of-line code - at least 11 zeros and a 1 - with any fill zeros before it.</summary>
    public bool SkipEndOfLine()
    {
        var at = _bit;
        var end = (long)_input.Length * 8;
        while (at < end && ((_input[(int)(at >> 3)] >> (7 - (int)(at & 7))) & 1) == 0)
        {
            at++;
        }

        if (at - _bit < 11 || at >= end)
        {
            return false;
        }

        _bit = at + 1;
        return true;
    }
}
