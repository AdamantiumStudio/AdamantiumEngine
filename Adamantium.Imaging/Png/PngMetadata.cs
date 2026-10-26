using System;
using System.Collections.Generic;

namespace Adamantium.Imaging.Png;

/// <summary>What a PNG says about its picture besides the pixels (PNG, 11.3): read from a file, and written with one.
/// Unset parts are null.</summary>
public sealed class PngMetadata
{
    /// <summary>bKGD: the color to show the picture on, as 16-bit samples - the palette's entry for a palette image.
    /// </summary>
    public (ushort Red, ushort Green, ushort Blue)? Background { get; set; }

    /// <summary>gAMA: the gamma the samples were encoded with.</summary>
    public double? Gamma { get; set; }

    /// <summary>cHRM: the CIE x, y chromaticities of the white point and the primaries.</summary>
    public PngChromaticities? Chromaticities { get; set; }

    /// <summary>sRGB: the picture is in sRGB, rendered with this intent.</summary>
    public PngRenderingIntent? SrgbIntent { get; set; }

    /// <summary>iCCP: the ICC profile's name; null without one.</summary>
    public string IccProfileName { get; set; }

    /// <summary>iCCP: the ICC profile, uncompressed.</summary>
    public byte[] IccProfile { get; set; }

    /// <summary>tEXt, zTXt and iTXt, in file order.</summary>
    public IList<PngText> Texts { get; } = new List<PngText>();

    /// <summary>pHYs: pixels per unit across and down, the unit a meter or none - an aspect ratio.</summary>
    public (uint X, uint Y, bool Meters)? PixelsPerUnit { get; set; }

    /// <summary>tIME: when the picture was last changed, UTC.</summary>
    public DateTime? Time { get; set; }

    /// <summary>eXIf: the Exif profile, as the file holds it.</summary>
    public byte[] Exif { get; set; }
}

/// <summary>The chromaticities of cHRM.</summary>
public readonly record struct PngChromaticities(double WhiteX, double WhiteY, double RedX, double RedY,
    double GreenX, double GreenY, double BlueX, double BlueY);

/// <summary>A text of tEXt, zTXt or iTXt: <paramref name="Keyword"/> and <paramref name="Text"/>, the latter in
/// <paramref name="Language"/> with a translated keyword for an international one.</summary>
public readonly record struct PngText(string Keyword, string Text, string Language = null,
    string TranslatedKeyword = null, bool International = false, bool Compressed = false);

/// <summary>The rendering intents of sRGB.</summary>
public enum PngRenderingIntent : byte
{
    Perceptual = 0,
    RelativeColorimetric = 1,
    Saturation = 2,
    AbsoluteColorimetric = 3
}

/// <summary>An APNG frame's place on the canvas, how long it shows and what happens to it after (fcTL).</summary>
public readonly record struct PngFrame(uint X, uint Y, uint Width, uint Height, ushort DelayNumerator,
    ushort DelayDenominator, PngDispose Dispose, PngBlend Blend)
{
    /// <summary>How long the frame shows: numerator over denominator seconds, a denominator of 0 meaning 100.</summary>
    public TimeSpan Delay => TimeSpan.FromSeconds(DelayNumerator / (double)(DelayDenominator == 0 ? 100 : DelayDenominator));
}

/// <summary>What becomes of a frame's region before the next is drawn.</summary>
public enum PngDispose : byte
{
    /// <summary>It stays.</summary>
    None = 0,

    /// <summary>It is cleared to transparent black.</summary>
    Background = 1,

    /// <summary>It goes back to what it was before the frame.</summary>
    Previous = 2
}

/// <summary>How a frame is drawn on the canvas.</summary>
public enum PngBlend : byte
{
    /// <summary>Its pixels replace the region's.</summary>
    Source = 0,

    /// <summary>Its pixels are composited over the region by their alpha.</summary>
    Over = 1
}
