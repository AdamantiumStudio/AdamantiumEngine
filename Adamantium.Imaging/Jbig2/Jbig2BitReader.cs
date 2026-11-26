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

    public long RemainingBits => (long)_end * 8 - _bit;

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

    /// <summary>The next 64 bits, not read, the first the highest; zeros past the end.</summary>
    public ulong Peek()
    {
        var at = _bit >> 3;
        ulong bits = 0;
        for (var index = 0; index < 8; index++)
        {
            bits = bits << 8 | ByteAt(at + index);
        }

        // From a bit within a byte: the ninth byte's highest bits make up the 64.
        var shift = (int)(_bit & 7);
        return shift == 0 ? bits : bits << shift | ByteAt(at + 8) >> (8 - shift);
    }

    private ulong ByteAt(long at) => at < _end ? _data[at] : 0u;

    /// <summary>Passes <paramref name="count"/> bits, which must be there.</summary>
    public void Skip(int count)
    {
        if (_bit + count > (long)_end * 8)
        {
            throw new InvalidDataException("Huffman-coded JBIG2 data ends early.");
        }

        _bit += count;
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
