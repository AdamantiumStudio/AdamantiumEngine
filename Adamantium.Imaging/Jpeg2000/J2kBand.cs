namespace Adamantium.Imaging.Jpeg2000;

/// <summary>A subband of a tile-component (ITU-T T.800, B.5): its bounds in its own coordinates, its code-block
/// size, its bit-planes and step, and the coefficients its code-blocks decode into.</summary>
internal sealed class J2kBand
{
    public const int LowLow = 0;
    public const int HighLow = 1;
    public const int LowHigh = 2;
    public const int HighHigh = 3;

    public J2kBand(int orientation, int level, int x0, int y0, int x1, int y1, int blockWidthExponent,
        int blockHeightExponent)
    {
        Orientation = orientation;
        Level = level;
        X0 = x0;
        Y0 = y0;
        X1 = x1;
        Y1 = y1;
        BlockWidthExponent = blockWidthExponent;
        BlockHeightExponent = blockHeightExponent;
    }

    public int Orientation { get; }

    /// <summary>The decomposition level nb.</summary>
    public int Level { get; }

    public int X0 { get; }

    public int Y0 { get; }

    public int X1 { get; }

    public int Y1 { get; }

    public int Width => X1 - X0;

    public int Height => Y1 - Y0;

    public int BlockWidthExponent { get; }

    public int BlockHeightExponent { get; }

    /// <summary>Mb: the magnitude bit-planes of its coefficients, a region of interest's shift included.</summary>
    public int Planes { get; set; }

    /// <summary>The quantization step; 1 for the reversible wavelet.</summary>
    public float Step { get; set; } = 1;

    /// <summary>The coefficients row after row, as the irreversible wavelet takes them.</summary>
    public float[] Values { get; set; }

    /// <summary>The coefficients row after row, as the reversible wavelet takes them.</summary>
    public int[] Integers { get; set; }
}
