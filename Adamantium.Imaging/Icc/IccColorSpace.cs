namespace Adamantium.Imaging.Icc;

/// <summary>The color spaces an ICC profile's device side or profile connection space can be in (ICC.1, table 19).
/// </summary>
public enum IccColorSpace
{
    Other,
    Gray,
    Rgb,
    Cmyk,
    Cmy,
    Lab,
    Xyz,
    YCbCr,
    Hsv,
    Hls,

    /// <summary>A space of 2 to 15 colorants with no name of its own.</summary>
    Colorants,
}
