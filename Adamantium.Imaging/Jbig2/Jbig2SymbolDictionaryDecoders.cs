namespace Adamantium.Imaging.Jbig2;

/// <summary>The arithmetic integer decoders of a symbol dictionary's own numbers: IADH, IADW, IAEX and IAAI.</summary>
internal sealed class Jbig2SymbolDictionaryDecoders
{
    public Jbig2IntegerDecoder Height { get; } = new();

    public Jbig2IntegerDecoder Width { get; } = new();

    public Jbig2IntegerDecoder Export { get; } = new();

    public Jbig2IntegerDecoder Aggregate { get; } = new();
}
