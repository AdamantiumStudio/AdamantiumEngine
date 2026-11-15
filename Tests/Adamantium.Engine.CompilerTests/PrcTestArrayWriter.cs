using System;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Writes the arrays of a PRC highly compressed tessellation as the specification's writing procedures give
/// them (WriteCharacterArray, WriteShortArray, WriteCompressedIntegerArray, WriteCompressedIndiceArray, Huffman): plain,
/// or Huffman coded - a tree built by merging the two rarest nodes, every code then put under the 1 branch of a root
/// whose 0 branch is left empty; the stream holding the number of leaves, the width of the code lengths, each leaf's
/// value, code length and code with the code's root bit last, the number of values and each value's code from the root,
/// packed lowest bit first into 32-bit words written as four Characters, with the bits used in the last word.</summary>
internal static class PrcTestArrayWriter
{
    public static void CharacterArray(PrcTestBitWriter writer, IReadOnlyList<int> values, int bitNumber, bool huffman, bool writeFlag = true)
    {
        if (writeFlag)
        {
            writer.WriteBoolean(huffman);
        }

        if (huffman)
        {
            HuffmanArray(writer, values, bitNumber);
            return;
        }

        writer.WriteUnsignedInteger((uint)values.Count);
        foreach (var value in values)
        {
            writer.WriteCharacter((byte)(value & 0xFF));
        }
    }

    public static void ShortArray(PrcTestBitWriter writer, IReadOnlyList<int> values, int bitNumber, bool huffman)
    {
        writer.WriteBoolean(huffman);
        if (huffman)
        {
            HuffmanArray(writer, values, bitNumber);
            return;
        }

        writer.WriteUnsignedInteger((uint)values.Count);
        foreach (var value in values)
        {
            writer.WriteCharacter((byte)(value & 0xFF));
            writer.WriteCharacter((byte)((value >> 8) & 0xFF));
        }
    }

    /// <summary>Each integer's width - one bit for the sign and as many as its magnitude needs - as a six-bit
    /// CharacterArray, then the integers in those widths.</summary>
    public static void CompressedIntegerArray(PrcTestBitWriter writer, IReadOnlyList<int> values, bool huffman)
    {
        var widths = values.Select(value => 1 + MagnitudeBits(Math.Abs(value))).ToList();
        CharacterArray(writer, widths, 6, huffman);
        for (var index = 0; index < values.Count; index++)
        {
            IntegerWithVariableBitNumber(writer, values[index], widths[index]);
        }
    }

    /// <summary>The first index's width and then how much each width differs from the one before, as a signed
    /// six-bit CharacterArray, then the first index and each difference to the next in those widths.</summary>
    public static void CompressedIndiceArray(PrcTestBitWriter writer, IReadOnlyList<int> values, bool huffman, bool writeFlag = true)
    {
        var differences = new List<int>();
        for (var index = 0; index < values.Count; index++)
        {
            differences.Add(index == 0 ? values[0] : values[index] - values[index - 1]);
        }

        var widths = differences.Select(difference => 1 + MagnitudeBits(Math.Abs(difference))).ToList();
        var steps = new List<int>();
        for (var index = 0; index < widths.Count; index++)
        {
            steps.Add(index == 0 ? widths[0] : widths[index] - widths[index - 1]);
        }

        CharacterArray(writer, steps, 6, huffman, writeFlag);
        for (var index = 0; index < differences.Count; index++)
        {
            IntegerWithVariableBitNumber(writer, differences[index], widths[index]);
        }
    }

    public static void IntegerWithVariableBitNumber(PrcTestBitWriter writer, int value, int bitNumber)
    {
        writer.WriteBoolean(value < 0);
        writer.WriteBits((uint)Math.Abs(value), bitNumber - 1);
    }

    public static int MagnitudeBits(int magnitude)
    {
        var bits = 0;
        while (magnitude >> bits != 0)
        {
            bits++;
        }

        return bits;
    }

    /// <summary>The Huffman words of <paramref name="values"/>, each of <paramref name="bitNumber"/> bits, with their
    /// count and the bits used in the last.</summary>
    public static void HuffmanArray(PrcTestBitWriter writer, IReadOnlyList<int> values, int bitNumber)
    {
        if (values.Count == 0)
        {
            writer.WriteUnsignedInteger(0);
            return;
        }

        var stream = new List<bool>();
        void Put(long value, int count)
        {
            for (var bit = 0; bit < count; bit++)
            {
                stream.Add(((value >> bit) & 1) != 0);
            }
        }

        var mask = (1 << bitNumber) - 1;
        var codes = BuildCodes(values.Select(value => value & mask).ToList());
        var lengthWidth = MagnitudeBits(codes.Values.Max(code => code.Length));
        Put(codes.Count, bitNumber + 1);
        Put(lengthWidth, 8);
        foreach (var leaf in codes.OrderBy(entry => entry.Key))
        {
            Put(leaf.Key, bitNumber);
            Put(leaf.Value.Length, lengthWidth);
            Put(leaf.Value.Value, leaf.Value.Length);
        }

        Put(values.Count, 32);
        foreach (var value in values)
        {
            var code = codes[value & mask];
            for (var bit = code.Length - 1; bit >= 0; bit--)
            {
                stream.Add(((code.Value >> bit) & 1) != 0);
            }
        }

        var words = (stream.Count + 31) / 32;
        writer.WriteUnsignedInteger((uint)words);
        for (var part = 0; part < words * 4; part++)
        {
            var character = 0;
            for (var bit = 0; bit < 8; bit++)
            {
                var at = part * 8 + bit;
                if (at < stream.Count && stream[at])
                {
                    character |= 1 << bit;
                }
            }

            writer.WriteCharacter((byte)character);
        }

        writer.WriteUnsignedInteger((uint)(stream.Count - (words - 1) * 32));
    }

    private static Dictionary<int, PrcTestCode> BuildCodes(IReadOnlyList<int> values)
    {
        var nodes = values.GroupBy(value => value).OrderBy(group => group.Count()).ThenBy(group => group.Key)
            .Select(group => new PrcTestNode { Weight = group.Count(), Value = group.Key }).ToList();
        while (nodes.Count > 1)
        {
            nodes = nodes.OrderBy(node => node.Weight).ToList();
            var zero = nodes[0];
            var one = nodes[1];
            nodes.RemoveRange(0, 2);
            nodes.Add(new PrcTestNode { Weight = zero.Weight + one.Weight, Zero = zero, One = one });
        }

        var codes = new Dictionary<int, PrcTestCode>();
        var pending = new Stack<PrcTestPending>();
        pending.Push(new PrcTestPending(nodes[0], 1, 1));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current.Node.Zero == null)
            {
                codes[current.Node.Value] = new PrcTestCode(current.Code, current.Length);
                continue;
            }

            pending.Push(new PrcTestPending(current.Node.Zero, current.Code << 1, current.Length + 1));
            pending.Push(new PrcTestPending(current.Node.One, (current.Code << 1) | 1, current.Length + 1));
        }

        return codes;
    }
}

/// <summary>A node of a test Huffman tree: its weight, and its value or its two branches.</summary>
internal sealed class PrcTestNode
{
    public int Weight { get; init; }

    public int Value { get; init; }

    public PrcTestNode Zero { get; init; }

    public PrcTestNode One { get; init; }
}

/// <summary>A node waiting to be given its code: the code so far, the root bit highest, and its length.</summary>
internal sealed class PrcTestPending
{
    public PrcTestPending(PrcTestNode node, long code, int length)
    {
        Node = node;
        Code = code;
        Length = length;
    }

    public PrcTestNode Node { get; }

    public long Code { get; }

    public int Length { get; }
}

/// <summary>A Huffman code: its bits, the root bit highest, and its length.</summary>
internal sealed class PrcTestCode
{
    public PrcTestCode(long value, int length)
    {
        Value = value;
        Length = length;
    }

    public long Value { get; }

    public int Length { get; }
}
