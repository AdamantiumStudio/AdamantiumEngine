namespace Adamantium.Pdf.Graphics;

/// <summary>How a drawn object is mixed with what lies under it (ISO 32000-2, 11.3.5): the separable modes first, then
/// the four non-separable ones.</summary>
public enum PdfBlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity,
}
