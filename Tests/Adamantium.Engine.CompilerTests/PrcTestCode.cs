namespace Adamantium.Engine.CompilerTests;

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
