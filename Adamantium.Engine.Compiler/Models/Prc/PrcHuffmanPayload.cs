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
