namespace Adamantium.Imaging.Jpeg;

/// <summary>One component of a JPEG frame (ITU-T T.81, A.1.1) and what decoding it produces: its samples, or for a
/// progressive frame its coefficients until the last scan.</summary>
internal sealed class JpegComponent
{
    public JpegComponent(int id, int horizontal, int vertical, int table)
    {
        Id = id;
        Horizontal = horizontal;
        Vertical = vertical;
        Table = table;
    }

    public int Id { get; }

    /// <summary>Sampling factors, 1 to 4.</summary>
    public int Horizontal { get; }

    public int Vertical { get; }

    /// <summary>The quantization table it is dequantized by.</summary>
    public int Table { get; }

    /// <summary>Samples across and down: the frame's size scaled by the factors, rounded up (A.1.1).</summary>
    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>Blocks that hold its samples, across and down - what a scan of it alone codes.</summary>
    public int BlocksAcross { get; set; }

    public int BlocksDown { get; set; }

    /// <summary>Blocks of the whole MCUs - what an interleaved scan codes; the plane holds this many.</summary>
    public int StoredAcross { get; set; }

    public int StoredDown { get; set; }

    /// <summary>The samples, <see cref="Stride"/> apart: 8-bit, or <see cref="Wide"/> for more bits. For the
    /// eighth-scale picture one sample a block.</summary>
    public byte[] Samples { get; set; }

    public ushort[] Wide { get; set; }

    public int Stride { get; set; }

    /// <summary>A progressive frame's coefficients, 64 a block in natural order, blocks in rows of
    /// <see cref="StoredAcross"/>.</summary>
    public short[] Coefficients { get; set; }

    public JpegHuffman Dc { get; set; }

    public JpegHuffman Ac { get; set; }

    /// <summary>The quantization table its coefficients are dequantized by, natural order.</summary>
    public ushort[] Quantization { get; set; }
}
