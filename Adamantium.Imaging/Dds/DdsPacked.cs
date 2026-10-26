namespace Adamantium.Imaging.Dds;

/// <summary>A layout of the older DDS header that no Vulkan format has; read widened to 8-bit RGBA.</summary>
internal enum DdsPacked
{
    None,

    /// <summary>3-3-2 RGB in a byte.</summary>
    Rgb332,

    /// <summary>3-3-2 RGB in the low byte, alpha in the high one.</summary>
    Argb8332,

    /// <summary>Luminance in the low four bits, alpha in the high four.</summary>
    LuminanceAlpha44,

    /// <summary>An index into the palette of 256 RGBA entries that follows the header.</summary>
    Palette,

    /// <summary>A palette index in the low byte, alpha in the high one.</summary>
    PaletteAlpha
}
