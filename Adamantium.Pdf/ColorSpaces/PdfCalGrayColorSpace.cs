using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>CalGray: one component A, luminance A^Gamma of the white point.</summary>
public sealed class PdfCalGrayColorSpace : PdfColorSpace
{
    private readonly double[] _whitePoint;
    private readonly double _gamma;

    public PdfCalGrayColorSpace(double[] whitePoint, double gamma)
    {
        _whitePoint = whitePoint;
        _gamma = gamma > 0 ? gamma : 1;
    }

    public override string Family => PdfColorSpaceFamilies.CalGray;

    public override int Components => 1;

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        var luminance = Math.Pow(Clamp01(At(components, 0)), _gamma);
        var wx = _whitePoint[0];
        var wy = _whitePoint[1];
        var wz = _whitePoint[2];
        PdfCie.XyzToSrgb(wx * luminance, wy * luminance, wz * luminance, _whitePoint, rgb);
    }
}
