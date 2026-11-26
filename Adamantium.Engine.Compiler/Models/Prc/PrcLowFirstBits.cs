using System;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>A stream of bits over bytes, the lowest bit of each byte first, that ends after its used bits.</summary>
internal sealed class PrcLowFirstBits
{
    private readonly byte[] _bytes;
    private readonly long _length;
    private readonly PrcBitReader _owner;
    private readonly string _what;
    private long _position;

    internal PrcLowFirstBits(byte[] bytes, long length, PrcBitReader owner, string what)
    {
        _bytes = bytes;
        _length = Math.Min(length, bytes.Length * 8L);
        _owner = owner;
        _what = what;
    }

    /// <summary>How many of the used bits are not read yet.</summary>
    internal long Remaining => _length - _position;

    internal bool ReadBit()
    {
        if (_position >= _length)
        {
            throw _owner.CreateCorruptException($"the Huffman coded {_what} ends before its codes do");
        }

        var bit = ((_bytes[_position >> 3] >> (int)(_position & 7)) & 1) != 0;
        _position++;
        return bit;
    }

    /// <summary>The next 64 bits, not read, the first the lowest; zeros past the used ones.</summary>
    internal ulong Peek()
    {
        var at = _position >> 3;
        ulong bits = 0;
        for (var index = 0; index < 8; index++)
        {
            bits |= ByteAt(at + index) << (8 * index);
        }

        var shift = (int)(_position & 7);
        if (shift != 0)
        {
            bits = bits >> shift | ByteAt(at + 8) << (64 - shift);
        }

        var left = Remaining;
        return left >= 64 ? bits : left <= 0 ? 0 : bits & ((1UL << (int)left) - 1);
    }

    /// <summary>Passes <paramref name="count"/> bits, which must be among the used ones.</summary>
    internal void Skip(int count)
    {
        if (count > Remaining)
        {
            throw _owner.CreateCorruptException($"the Huffman coded {_what} ends before its codes do");
        }

        _position += count;
    }

    private ulong ByteAt(long at) => at < _bytes.Length ? _bytes[at] : 0u;

    /// <summary><paramref name="count"/> bits, up to 32, the first read the lowest.</summary>
    internal uint Read(int count)
    {
        var value = 0u;
        for (var bit = 0; bit < count; bit++)
        {
            if (ReadBit())
            {
                value |= 1u << bit;
            }
        }

        return value;
    }
}
