namespace Adamantium.Imaging.Jpeg2000;

/// <summary>The color space a JPEG 2000 file says its channels are in.</summary>
public enum Jpeg2000ColorSpace
{
    /// <summary>A bare codestream or a file that does not say; the channel count is all there is.</summary>
    Unknown,

    Gray,

    Srgb,

    /// <summary>sYCC; the decoder has turned it into sRGB.</summary>
    Sycc,

    Cmyk,

    /// <summary>An ICC profile, in <see cref="Jpeg2000Image.IccProfile"/>.</summary>
    Icc,
}
