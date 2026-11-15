using System;
using System.Collections.Generic;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>The words of a Huffman coded array as the file stores them: four Characters a word, its lowest byte
/// first, and how many of its bits are in use - all but the last word's unused high bits.</summary>
internal sealed class PrcHuffmanPayload
{
    internal PrcHuffmanPayload(byte[] bytes, long usedBits)
    {
        Bytes = bytes;
        UsedBits = usedBits;
    }

    internal byte[] Bytes { get; }

    internal long UsedBits { get; }
}

/// <summary>Reads a Huffman coded array (PRC Huffman Algorithm): its words make one stream of bits, the lowest bit of
/// each byte first. The stream holds the number of leaves in one bit more than a value takes, the width the code
/// lengths are stored in as eight bits, then for each leaf its value, its code length and its code, the code's
/// first bit lowest; then the number of values in 32 bits, and each value's code with the code's highest bit first.
/// The tree is rebuilt from the leaves' codes, and the codes are walked until that number of values is read.</summary>
internal static class PrcHuffmanDecoder
{
    private const int NoChild = -1;
    private const int CountBits = 32;
    private const int LongestCode = 32;

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

        var lengthWidth = (int)stream.Read(8);
        if (lengthWidth < 1 || lengthWidth > LongestCode)
        {
            throw owner.Corrupt($"the Huffman coded {what} stores its code lengths in {lengthWidth} bits");
        }

        var tree = new PrcHuffmanTree();
        for (var leaf = 0; leaf < leaves; leaf++)
        {
            var value = (int)stream.Read(bitNumber);
            if (signed && bitNumber < 32 && (value & (1 << (bitNumber - 1))) != 0)
            {
                value -= 1 << bitNumber;
            }

            var length = (int)stream.Read(lengthWidth);
            if (length > LongestCode)
            {
                throw owner.Corrupt($"a code of the Huffman coded {what} is {length} bits long");
            }

            var code = stream.Read(length);
            if (!tree.Add(code, length, value))
            {
                throw owner.Corrupt($"the codes of the Huffman coded {what} are not a prefix code");
            }
        }

        var count = stream.Read(CountBits);
        if ((decimal)count * tree.ShortestCode > stream.Remaining)
        {
            throw owner.Corrupt($"the Huffman coded {what} names {count} values, more than its {stream.Remaining} bits of codes can hold");
        }
        var values = new int[count];
        for (var index = 0; index < values.Length; index++)
        {
            var node = 0;
            while (!tree.IsLeaf(node))
            {
                node = tree.Child(node, stream.ReadBit());
                if (node == NoChild)
                {
                    throw owner.Corrupt($"the Huffman coded {what} holds a code no leaf has");
                }
            }

            values[index] = tree.ValueOf(node);
        }

        return values;
    }

    private sealed class PrcHuffmanTree
    {
        private readonly List<int> zeroChild = [NoChild];
        private readonly List<int> oneChild = [NoChild];
        private readonly List<int> leafValue = [0];
        private readonly List<bool> isLeaf = [false];

        internal int ShortestCode { get; private set; } = LongestCode;

        internal bool IsLeaf(int node) => isLeaf[node];

        internal int Child(int node, bool bit) => bit ? oneChild[node] : zeroChild[node];

        internal int ValueOf(int node) => leafValue[node];

        internal bool Add(uint code, int length, int value)
        {
            if (length == 0)
            {
                if (zeroChild[0] != NoChild || oneChild[0] != NoChild || isLeaf[0])
                {
                    return false;
                }

                isLeaf[0] = true;
                leafValue[0] = value;
                ShortestCode = 0;
                return true;
            }

            var node = 0;
            for (var bit = length - 1; bit >= 0; bit--)
            {
                if (isLeaf[node])
                {
                    return false;
                }

                var one = ((code >> bit) & 1) != 0;
                var next = Child(node, one);
                if (next == NoChild)
                {
                    next = isLeaf.Count;
                    zeroChild.Add(NoChild);
                    oneChild.Add(NoChild);
                    leafValue.Add(0);
                    isLeaf.Add(false);
                    if (one)
                    {
                        oneChild[node] = next;
                    }
                    else
                    {
                        zeroChild[node] = next;
                    }
                }
                else if (bit == 0)
                {
                    return false;
                }

                node = next;
            }

            isLeaf[node] = true;
            leafValue[node] = value;
            ShortestCode = Math.Min(ShortestCode, length);
            return true;
        }
    }
}

/// <summary>A stream of bits over bytes, the lowest bit of each byte first, that ends after its used bits.</summary>
internal sealed class PrcLowFirstBits
{
    private readonly byte[] bytes;
    private readonly long length;
    private readonly PrcBitReader owner;
    private readonly string what;
    private long position;

    internal PrcLowFirstBits(byte[] bytes, long length, PrcBitReader owner, string what)
    {
        this.bytes = bytes;
        this.length = Math.Min(length, bytes.Length * 8L);
        this.owner = owner;
        this.what = what;
    }

    /// <summary>How many of the used bits are not read yet.</summary>
    internal long Remaining => length - position;

    internal bool ReadBit()
    {
        if (position >= length)
        {
            throw owner.Corrupt($"the Huffman coded {what} ends before its codes do");
        }

        var bit = ((bytes[position >> 3] >> (int)(position & 7)) & 1) != 0;
        position++;
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
