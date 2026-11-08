using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>DeviceGray: one component, 0 black to 1 white.</summary>
public sealed class PdfDeviceGrayColorSpace : PdfColorSpace
{
    public static PdfDeviceGrayColorSpace Instance { get; } = new();

    public override string Family => "DeviceGray";

    public override int Components => 1;

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        var gray = Clamp01(At(components, 0));
        rgb[0] = gray;
        rgb[1] = gray;
        rgb[2] = gray;
    }
}
