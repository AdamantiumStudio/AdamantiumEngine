namespace Adamantium.Imaging.Tiff;

/// <summary>How TIFF pixel data is compressed.</summary>
public enum TiffCompression : ushort
{
    /// <summary>Not compressed.</summary>
    None = 1,

    /// <summary>CCITT modified Huffman run lengths, 1 bit.</summary>
    CcittRle = 2,

    /// <summary>CCITT Group 3 fax, 1 bit.</summary>
    CcittGroup3 = 3,

    /// <summary>CCITT Group 4 fax, 1 bit.</summary>
    CcittGroup4 = 4,

    /// <summary>LZW.</summary>
    Lzw = 5,

    /// <summary>Old-style JPEG (TIFF 6.0, section 22).</summary>
    OldJpeg = 6,

    /// <summary>JPEG (TIFF Technical Note 2).</summary>
    Jpeg = 7,

    /// <summary>Deflate, zlib-wrapped, under the code Adobe registered.</summary>
    Deflate = 8,

    /// <summary>Deflate under its earlier, unregistered code; read, never written.</summary>
    OldDeflate = 32946,

    /// <summary>PackBits run lengths.</summary>
    PackBits = 32773,

    /// <summary>LERC (Esri) - not read: there is no published specification.</summary>
    Lerc = 34887,

    /// <summary>Zstandard.</summary>
    Zstd = 50000,

    /// <summary>WebP.</summary>
    WebP = 50001
}
