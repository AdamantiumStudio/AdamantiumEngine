using System.Collections.Generic;

namespace Adamantium.PdfTests;

/// <summary>Packs values of a given number of bits, most significant first, as mesh shadings store them.</summary>
internal sealed class BitPacker
{
    private readonly List<byte> _bytes = [];
    private int _bit;

    public BitPacker Put(int value, int bits)
    {
        for (var i = bits - 1; i >= 0; i--)
        {
            if (_bit == 0)
            {
                _bytes.Add(0);
            }

            if (((value >> i) & 1) != 0)
            {
                _bytes[^1] |= (byte)(0x80 >> _bit);
            }

            _bit = (_bit + 1) & 7;
        }

        return this;
    }

    /// <summary>Starts the next value on a new byte.</summary>
    public BitPacker Align()
    {
        _bit = 0;
        return this;
    }

    public byte[] ToArray() => _bytes.ToArray();
}
