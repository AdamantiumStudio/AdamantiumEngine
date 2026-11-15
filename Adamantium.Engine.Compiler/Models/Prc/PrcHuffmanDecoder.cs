namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Reads a Huffman coded array (PRC Huffman Algorithm): rebuilds its tree from the stored leaves and walks the
/// codes until the stored number of values is read.</summary>
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

        var lengthWidth = (int)stream.Read(8);
        if (lengthWidth < 1 || lengthWidth > PrcHuffmanTree.LongestCode)
        {
            throw owner.CreateCorruptException($"the Huffman coded {what} stores its code lengths in {lengthWidth} bits");
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
            if (length > PrcHuffmanTree.LongestCode)
            {
                throw owner.CreateCorruptException($"a code of the Huffman coded {what} is {length} bits long");
            }

            var code = stream.Read(length);
            if (!tree.Add(code, length, value))
            {
                throw owner.CreateCorruptException($"the codes of the Huffman coded {what} are not a prefix code");
            }
        }

        var count = stream.Read(CountBits);
        if ((decimal)count * tree.ShortestCode > stream.Remaining)
        {
            throw owner.CreateCorruptException($"the Huffman coded {what} names {count} values, more than its {stream.Remaining} bits of codes can hold");
        }

        if (tree.ShortestCode == 0)
        {
            owner.CheckCount(count, 1, $"values of the Huffman coded {what}");
        }

        var values = new int[count];
        for (var index = 0; index < values.Length; index++)
        {
            var node = 0;
            while (!tree.IsLeaf(node))
            {
                node = tree.GetChild(node, stream.ReadBit());
                if (node == PrcHuffmanTree.NoChild)
                {
                    throw owner.CreateCorruptException($"the Huffman coded {what} holds a code no leaf has");
                }
            }

            values[index] = tree.GetValue(node);
        }

        return values;
    }
}
