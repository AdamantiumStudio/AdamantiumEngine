using System.IO;

namespace Adamantium.Imaging.Jbig2;

/// <summary>Reads the bits of Huffman-coded JBIG2 data, most significant first.</summary>
internal sealed class Jbig2BitReader
{
    private readonly byte[] _data;
    private readonly int _end;
    private long _bit;

    public Jbig2BitReader(byte[] data, int start, int end)
    {
        _data = data;
        _end = end;
        _bit = (long)start * 8;
    }

    public byte[] Data => _data;

    public int End => _end;

    /// <summary>The byte the next bit is in, or the next byte after a byte boundary.</summary>
    public int BytePosition => (int)((_bit + 7) >> 3);

    public int ReadBit()
    {
        var at = _bit >> 3;
        if (at >= _end)
        {
            throw new InvalidDataException("Huffman-coded JBIG2 data ends early.");
        }

        var bit = (_data[at] >> (7 - (int)(_bit & 7))) & 1;
        _bit++;
        return bit;
    }

    /// <summary>The next <paramref name="count"/> bits, up to 32, as an unsigned number.</summary>
    public long ReadBits(int count)
    {
        long value = 0;
        for (var i = 0; i < count; i++)
        {
            value = (value << 1) | (uint)ReadBit();
        }

        return value;
    }

    public void AlignToByte() => _bit = (_bit + 7) & ~7L;

    /// <summary>Moves to byte <paramref name="position"/>.</summary>
    public void MoveTo(int position) => _bit = (long)position * 8;
}
