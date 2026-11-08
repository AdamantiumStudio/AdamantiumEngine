using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>DeviceRGB: red, green and blue, each 0 to 1, read as sRGB.</summary>
public sealed class PdfDeviceRgbColorSpace : PdfColorSpace
{
    public static PdfDeviceRgbColorSpace Instance { get; } = new();

    public override string Family => "DeviceRGB";

    public override int Components => 3;

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        rgb[0] = Clamp01(At(components, 0));
        rgb[1] = Clamp01(At(components, 1));
        rgb[2] = Clamp01(At(components, 2));
    }
}
