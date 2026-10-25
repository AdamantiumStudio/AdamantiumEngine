namespace Adamantium.Imaging.Tiff;

/// <summary>The type of a TIFF field's values (TIFF 6.0, section 2; the 64-bit types are BigTIFF's).</summary>
internal enum TiffFieldType : ushort
{
    /// <summary>Unsigned 8-bit integer.</summary>
    Byte = 1,

    /// <summary>7-bit ASCII text, each string ended by a zero byte.</summary>
    Ascii = 2,

    /// <summary>Unsigned 16-bit integer.</summary>
    Short = 3,

    /// <summary>Unsigned 32-bit integer.</summary>
    Long = 4,

    /// <summary>Two unsigned 32-bit integers: numerator, then denominator.</summary>
    Rational = 5,

    /// <summary>Signed 8-bit integer.</summary>
    SByte = 6,

    /// <summary>Bytes whose meaning the field defines.</summary>
    Undefined = 7,

    /// <summary>Signed 16-bit integer.</summary>
    SShort = 8,

    /// <summary>Signed 32-bit integer.</summary>
    SLong = 9,

    /// <summary>Two signed 32-bit integers: numerator, then denominator.</summary>
    SRational = 10,

    /// <summary>IEEE 32-bit float.</summary>
    Float = 11,

    /// <summary>IEEE 64-bit float.</summary>
    Double = 12,

    /// <summary>A 32-bit offset of a directory.</summary>
    Ifd = 13,

    /// <summary>Unsigned 64-bit integer (BigTIFF).</summary>
    Long8 = 16,

    /// <summary>Signed 64-bit integer (BigTIFF).</summary>
    SLong8 = 17,

    /// <summary>A 64-bit offset of a directory (BigTIFF).</summary>
    Ifd8 = 18
}
