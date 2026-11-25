namespace Adamantium.Pdf.Writing;

/// <summary>A color where a gradient reaches <see cref="Offset"/>, 0 at its start and 1 at its end: three components
/// 0..1 in DeviceRGB, or - for a gray shading - <see cref="Red"/> alone.</summary>
public readonly record struct PdfGradientStop(double Offset, double Red, double Green, double Blue)
{
    /// <summary>A gray stop - an alpha ramp for a soft mask.</summary>
    public static PdfGradientStop Gray(double offset, double value) => new(offset, value, value, value);
}
