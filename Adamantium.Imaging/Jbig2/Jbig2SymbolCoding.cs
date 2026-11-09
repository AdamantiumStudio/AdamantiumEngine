namespace Adamantium.Imaging.Jbig2;

/// <summary>How a symbol dictionary codes its symbol bitmaps (ITU-T T.88, 7.4.2.1.1).</summary>
internal sealed class Jbig2SymbolCoding
{
    public bool Huffman { get; init; }

    public bool RefineAggregate { get; init; }

    public int Template { get; init; }

    public int[] At { get; init; }

    public int RefinementTemplate { get; init; }

    public int[] RefinementAt { get; init; }

    /// <summary>The bits of a symbol number among the dictionary's input and new symbols.</summary>
    public int CodeLength { get; init; }

    /// <summary>The Huffman table of the number of symbols in an aggregate.</summary>
    public Jbig2HuffmanTable AggregateTable { get; init; }
}
