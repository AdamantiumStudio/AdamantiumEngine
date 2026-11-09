using System;
using System.Collections.Generic;
using System.IO;

namespace Adamantium.Imaging.Jbig2;

/// <summary>A Huffman table of JBIG2 with its prefix codes assigned (ITU-T T.88, B.3): shorter prefixes first, lines of
/// one length in their order.</summary>
internal sealed class Jbig2HuffmanTable
{
    private const int MaxPrefixLength = 32;

    private readonly long[] _firstCode = new long[MaxPrefixLength + 1];
    private readonly int[] _count = new int[MaxPrefixLength + 1];
    private readonly int[] _start = new int[MaxPrefixLength + 1];
    private readonly Jbig2HuffmanLine[] _byCode;
    private readonly int _longest;

    public Jbig2HuffmanTable(IReadOnlyList<Jbig2HuffmanLine> lines)
    {
        foreach (var line in lines)
        {
            if (line.PrefixLength is < 0 or > MaxPrefixLength || line.RangeLength is < 0 or > 32)
            {
                throw new InvalidDataException("A JBIG2 Huffman table has a line it cannot have.");
            }

            _count[line.PrefixLength]++;
            _longest = Math.Max(_longest, line.PrefixLength);
        }

        _count[0] = 0;
        long code = 0;
        var placed = 0;
        for (var length = 1; length <= _longest; length++)
        {
            code = (code + _count[length - 1]) << 1;
            if (code + _count[length] > 1L << length)
            {
                throw new InvalidDataException("A JBIG2 Huffman table has more codes than its prefixes allow.");
            }

            _firstCode[length] = code;
            _start[length] = placed;
            placed += _count[length];
        }

        IsComplete = _longest > 0 && code + _count[_longest] == 1L << _longest;

        _byCode = new Jbig2HuffmanLine[placed];
        var filled = new int[MaxPrefixLength + 1];
        foreach (var line in lines)
        {
            if (line.PrefixLength > 0)
            {
                _byCode[_start[line.PrefixLength] + filled[line.PrefixLength]++] = line;
            }
        }
    }

    /// <summary>Whether every bit string starts with one of the table's codes.</summary>
    public bool IsComplete { get; }

    /// <summary>The next value, or null for out-of-band.</summary>
    public int? Decode(Jbig2BitReader reader)
    {
        long code = 0;
        for (var length = 1; length <= _longest; length++)
        {
            code = (code << 1) | (uint)reader.ReadBit();
            var index = code - _firstCode[length];
            if (index >= 0 && index < _count[length])
            {
                return Value(_byCode[_start[length] + (int)index], reader);
            }
        }

        throw new InvalidDataException("JBIG2 data has a code its Huffman table does not.");
    }

    private static int? Value(Jbig2HuffmanLine line, Jbig2BitReader reader)
    {
        if (line.IsOutOfBand)
        {
            return null;
        }

        var offset = reader.ReadBits(line.RangeLength);
        var value = line.IsLower ? line.RangeLow - offset : line.RangeLow + offset;
        if (value is < int.MinValue or > int.MaxValue)
        {
            throw new InvalidDataException("A JBIG2 Huffman value is out of range.");
        }

        return (int)value;
    }

    /// <summary>Reads a table segment (ITU-T T.88, 7.4.13 and B.2).</summary>
    public static Jbig2HuffmanTable Read(BigEndianReader bytes)
    {
        var flags = bytes.ReadByte();
        var outOfBand = (flags & 1) != 0;
        var prefixBits = ((flags >> 1) & 7) + 1;
        var rangeBits = ((flags >> 4) & 7) + 1;
        var low = bytes.ReadInt32();
        var high = bytes.ReadInt32();
        var reader = new Jbig2BitReader(bytes.Data, bytes.Position, bytes.End);
        var lines = new List<Jbig2HuffmanLine>();
        long current = low;
        while (current < high)
        {
            var prefix = (int)reader.ReadBits(prefixBits);
            var range = (int)reader.ReadBits(rangeBits);
            if (range > 31 || lines.Count > 1 << 16)
            {
                throw new InvalidDataException("A JBIG2 Huffman table segment has a line it cannot have.");
            }

            lines.Add(new Jbig2HuffmanLine(prefix, range, (int)current));
            current += 1L << range;
        }

        lines.Add(Jbig2HuffmanLine.Lower((int)reader.ReadBits(prefixBits), low - 1));
        lines.Add(Jbig2HuffmanLine.Upper((int)reader.ReadBits(prefixBits), high));
        if (outOfBand)
        {
            lines.Add(Jbig2HuffmanLine.OutOfBand((int)reader.ReadBits(prefixBits)));
        }

        bytes.Position = Math.Min(bytes.End, reader.BytePosition);
        return new Jbig2HuffmanTable(lines);
    }
}
