namespace Adamantium.Imaging.Dds;

/// <summary>How an older DDS format few devices sample is widened as it is read: to 8-bit RGBA, 16-bit luminance to
/// 16-bit RGBA.</summary>
internal enum DdsWidening
{
    None,
    Bgr,
    Rgb,
    Argb4444,
    Luminance,
    LuminanceAlpha,
    Alpha,
    Luminance16,
    Rgb332,
    Argb8332,
    LuminanceAlpha44,
    Palette,
    PaletteAlpha
}
