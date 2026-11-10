using System;

namespace Adamantium.Pdf.Shadings;

/// <summary>Reads unsigned values of 1 to 32 bits, most significant bit first, as mesh shadings pack them.</summary>
internal ref struct PdfBitReader
{
    private readonly ReadOnlySpan<byte> _data;
    private long _bit;

    public PdfBitReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _bit = 0;
    }

    /// <summary>Whether <paramref name="bits"/> more bits are there to read.</summary>
    public bool Has(long bits) => _bit + bits <= (long)_data.Length * 8;

    public uint Read(int bits)
    {
        uint value = 0;
        for (var i = 0; i < bits; i++)
        {
            var at = _bit++;
            value = (value << 1) | (uint)((_data[(int)(at >> 3)] >> (7 - (int)(at & 7))) & 1);
        }

        return value;
    }

    /// <summary>Skips to the start of the next byte.</summary>
    public void Align() => _bit = (_bit + 7) & ~7L;
}
