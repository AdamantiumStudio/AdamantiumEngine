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
