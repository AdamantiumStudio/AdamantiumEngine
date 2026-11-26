using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Core.Compression;

namespace Adamantium.Imaging.Jbig2;

/// <summary>A Huffman table of JBIG2 with its prefix codes assigned (ITU-T T.88, B.3): shorter prefixes first, lines of
/// one length in their order - the canonical codes of a <see cref="PrefixCode"/>, a line's index its symbol. A line of
/// no prefix has no code.</summary>
internal sealed class Jbig2HuffmanTable
{
    private readonly Jbig2HuffmanLine[] _lines;
    private readonly PrefixCode _code;

    public Jbig2HuffmanTable(IReadOnlyList<Jbig2HuffmanLine> lines)
    {
        var lengths = new int[lines.Count];
        for (var index = 0; index < lengths.Length; index++)
        {
            var line = lines[index];
            if (line.PrefixLength is < 0 or > PrefixCode.LongestCode || line.RangeLength is < 0 or > 32)
            {
                throw new InvalidDataException("A JBIG2 Huffman table has a line it cannot have.");
            }

            lengths[index] = line.PrefixLength;
        }

        if (!PrefixCode.TryCanonical(lengths, out var words) ||
            !PrefixCode.TryCreate(words, PrefixBitOrder.HighFirst, out _code))
        {
            throw new InvalidDataException("A JBIG2 Huffman table has more codes than its prefixes allow.");
        }

        _lines = [.. lines];
    }

    /// <summary>Whether every bit string starts with one of the table's codes.</summary>
    public bool IsComplete => _code.IsComplete;

    public IReadOnlyList<Jbig2HuffmanLine> Lines => _lines;

    /// <summary>The next value, or null for out-of-band.</summary>
    public int? Decode(Jbig2BitReader reader)
    {
        var line = _code.Decode(reader.Peek(), out var length);
        if (line < 0)
        {
            throw new InvalidDataException("JBIG2 data has a code its Huffman table does not.");
        }

        reader.Skip(length);
        return Value(_lines[line], reader);
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
