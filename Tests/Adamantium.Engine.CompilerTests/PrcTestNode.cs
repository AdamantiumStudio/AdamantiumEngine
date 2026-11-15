namespace Adamantium.Engine.CompilerTests;

/// <summary>A node of a test Huffman tree: its weight, and its value or its two branches.</summary>
internal sealed class PrcTestNode
{
    public int Weight { get; init; }

    public int Value { get; init; }

    public PrcTestNode Zero { get; init; }

    public PrcTestNode One { get; init; }
}
