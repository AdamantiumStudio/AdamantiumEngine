namespace Adamantium.Imaging.Jbig2;

/// <summary>The arithmetic decoders a text region reads its numbers with; a symbol dictionary shares them with the
/// text regions of its aggregate symbols.</summary>
internal sealed class Jbig2TextDecoders
{
    public Jbig2TextDecoders(int symbolCodeLength, int refinementTemplate, byte[] refinementContexts = null)
    {
        SymbolId = new Jbig2SymbolIdDecoder(symbolCodeLength);
        RefinementContexts = refinementContexts ?? new byte[Jbig2RefinementRegion.ContextCount(refinementTemplate)];
    }

    public Jbig2IntegerDecoder DeltaT { get; } = new();

    public Jbig2IntegerDecoder FirstS { get; } = new();

    public Jbig2IntegerDecoder DeltaS { get; } = new();

    public Jbig2IntegerDecoder StripT { get; } = new();

    public Jbig2IntegerDecoder Refine { get; } = new();

    public Jbig2IntegerDecoder RefinementWidth { get; } = new();

    public Jbig2IntegerDecoder RefinementHeight { get; } = new();

    public Jbig2IntegerDecoder RefinementX { get; } = new();

    public Jbig2IntegerDecoder RefinementY { get; } = new();

    public Jbig2SymbolIdDecoder SymbolId { get; }

    public byte[] RefinementContexts { get; }
}
