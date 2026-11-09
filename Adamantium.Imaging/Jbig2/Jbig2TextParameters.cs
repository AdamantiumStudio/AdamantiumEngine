using System.Collections.Generic;

namespace Adamantium.Imaging.Jbig2;

/// <summary>The parameters of text region decoding (ITU-T T.88, table 9).</summary>
internal sealed class Jbig2TextParameters
{
    public bool Huffman { get; init; }

    public bool Refine { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }

    public long Instances { get; init; }

    public int LogStrips { get; init; }

    public IReadOnlyList<Jbig2Bitmap> Symbols { get; init; }

    public int SymbolCodeLength { get; init; }

    /// <summary>The symbol numbers' Huffman code, when <see cref="Huffman"/>.</summary>
    public Jbig2HuffmanTable SymbolCodes { get; init; }

    public int DefaultPixel { get; init; }

    public Jbig2Combination Combination { get; init; }

    public bool Transposed { get; init; }

    public Jbig2Corner Corner { get; init; }

    public int DsOffset { get; init; }

    public Jbig2HuffmanTable FirstS { get; init; }

    public Jbig2HuffmanTable DeltaS { get; init; }

    public Jbig2HuffmanTable DeltaT { get; init; }

    public Jbig2HuffmanTable RefinementWidth { get; init; }

    public Jbig2HuffmanTable RefinementHeight { get; init; }

    public Jbig2HuffmanTable RefinementX { get; init; }

    public Jbig2HuffmanTable RefinementY { get; init; }

    public Jbig2HuffmanTable RefinementSize { get; init; }

    public int RefinementTemplate { get; init; }

    public int[] RefinementAt { get; init; }
}
