namespace Adamantium.Imaging.Icc;

/// <summary>How colors outside what the destination shows are brought into it (ICC.1, 7.2.15).</summary>
public enum IccRenderingIntent
{
    Perceptual = 0,
    RelativeColorimetric = 1,
    Saturation = 2,
    AbsoluteColorimetric = 3,
}
