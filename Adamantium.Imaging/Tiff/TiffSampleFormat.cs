namespace Adamantium.Imaging.Tiff;

/// <summary>The kind of a TIFF sample's value.</summary>
public enum TiffSampleFormat : ushort
{
    /// <summary>Unsigned integer.</summary>
    UnsignedInteger = 1,

    /// <summary>Two's complement signed integer.</summary>
    SignedInteger = 2,

    /// <summary>IEEE floating point.</summary>
    Float = 3,

    /// <summary>Undefined; read as unsigned integer.</summary>
    Undefined = 4
}
