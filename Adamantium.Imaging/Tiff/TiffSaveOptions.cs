namespace Adamantium.Imaging.Tiff;

/// <summary>How <see cref="TiffEncoder"/> writes a TIFF file.</summary>
public sealed class TiffSaveOptions
{
    /// <summary>The compression: <see cref="TiffCompression.None"/>, <see cref="TiffCompression.PackBits"/>,
    /// <see cref="TiffCompression.Lzw"/> or <see cref="TiffCompression.Deflate"/>. Deflate by default: lossless and read
    /// everywhere.</summary>
    public TiffCompression Compression { get; set; } = TiffCompression.Deflate;

    /// <summary>Whether LZW and Deflate are given differences - horizontal for integers, floating-point for floats -
    /// which usually packs pictures tighter. On by default.</summary>
    public bool UsePredictor { get; set; } = true;

    /// <summary>The side of square tiles, a multiple of 16; 0 writes strips. 0 by default.</summary>
    public int TileSize { get; set; }

    /// <summary>Writes BigTIFF even when the file would fit classic TIFF - for checking the BigTIFF path.</summary>
    internal bool ForceBigTiff { get; set; }
}
