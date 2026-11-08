using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>DeviceCMYK: cyan, magenta, yellow and black inks, each 0 to 1. Shown by the plain conversion the format
/// describes (ISO 32000-2, 10.4.2.4) until colors are managed by profile.</summary>
public sealed class PdfDeviceCmykColorSpace : PdfColorSpace
{
    public static PdfDeviceCmykColorSpace Instance { get; } = new();

    public override string Family => "DeviceCMYK";

    public override int Components => 4;

    /// <summary>Black: no colored ink, all of the black.</summary>
    public override void InitialColor(Span<double> components)
    {
        components.Clear();
        components[3] = 1;
    }

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        var black = Clamp01(At(components, 3));
        rgb[0] = 1 - Math.Min(1, Clamp01(At(components, 0)) + black);
        rgb[1] = 1 - Math.Min(1, Clamp01(At(components, 1)) + black);
        rgb[2] = 1 - Math.Min(1, Clamp01(At(components, 2)) + black);
    }
}
