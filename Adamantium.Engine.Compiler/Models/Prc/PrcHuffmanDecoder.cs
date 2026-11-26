using System.Collections.Generic;
using Adamantium.Core.Compression;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads a Huffman coded array (PRC Huffman Algorithm): makes a <see cref="PrefixCode"/> of the stored leaves
/// and reads codes until the stored number of values is read.</summary>
internal static class PrcHuffmanDecoder
{
    private const int CountBits = 32;

    /// <summary>The values <paramref name="payload"/> holds, each of <paramref name="bitNumber"/> bits, read as two's
    /// complement in that width when <paramref name="signed"/>; corrupt codes throw at the position of
    /// <paramref name="owner"/>, naming <paramref name="what"/>.</summary>
    internal static int[] Decode(PrcHuffmanPayload payload, int bitNumber, bool signed, PrcBitReader owner, string what)
    {
        if (payload.Bytes.Length == 0)
        {
            return [];
        }

        var stream = new PrcLowFirstBits(payload.Bytes, payload.UsedBits, owner, what);
        var leaves = (int)stream.Read(bitNumber + 1);
        if (leaves == 0)
        {
            return [];
        }

        // Each leaf's index is its symbol.
        if (leaves > PrefixCode.MostSymbols)
        {
            throw owner.CreateCorruptException($"the Huffman coded {what} has {leaves} leaves, more than {PrefixCode.MostSymbols}");
        }

        var lengthWidth = (int)stream.Read(8);
        if (lengthWidth < 1 || lengthWidth > PrefixCode.LongestCode)
        {
            throw owner.CreateCorruptException($"the Huffman coded {what} stores its code lengths in {lengthWidth} bits");
        }

        // A leaf's code is read the first bit the lowest, and decoded the highest first: a code of the stream's order.
        var words = new List<PrefixCodeWord>();
        var leafValues = new List<int>();
        for (var leaf = 0; leaf < leaves; leaf++)
        {
            var value = (int)stream.Read(bitNumber);
            if (signed && bitNumber < 32 && (value & (1 << (bitNumber - 1))) != 0)
            {
                value -= 1 << bitNumber;
            }

            var length = (int)stream.Read(lengthWidth);
            if (length > PrefixCode.LongestCode)
            {
                throw owner.CreateCorruptException($"a code of the Huffman coded {what} is {length} bits long");
            }

            words.Add(new PrefixCodeWord(stream.Read(length), length, leaf));
            leafValues.Add(value);
        }

        if (!PrefixCode.TryCreate(words.ToArray(), PrefixBitOrder.LowFirst, out var code))
        {
            throw owner.CreateCorruptException($"the codes of the Huffman coded {what} are not a prefix code");
        }

        var count = stream.Read(CountBits);
        if ((decimal)count * code.Shortest > stream.Remaining)
        {
            throw owner.CreateCorruptException($"the Huffman coded {what} names {count} values, more than its {stream.Remaining} bits of codes can hold");
        }

        if (code.Shortest == 0)
        {
            owner.CheckCount(count, 1, $"values of the Huffman coded {what}");
        }

        var values = new int[count];
        for (var index = 0; index < values.Length; index++)
        {
            var leaf = code.Decode(stream.Peek(), out var length);
            if (leaf < 0)
            {
                throw owner.CreateCorruptException(stream.Remaining < code.Longest
                    ? $"the Huffman coded {what} ends before its codes do"
                    : $"the Huffman coded {what} holds a code no leaf has");
            }

            stream.Skip(length);
            values[index] = leafValues[leaf];
        }

        return values;
    }
}
