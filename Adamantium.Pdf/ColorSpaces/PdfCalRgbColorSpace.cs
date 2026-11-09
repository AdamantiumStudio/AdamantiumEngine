using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>CalRGB: components A, B and C raised to their gammas, then <c>/Matrix</c> to CIE XYZ.</summary>
public sealed class PdfCalRgbColorSpace : PdfColorSpace
{
    private readonly double[] _whitePoint;
    private readonly double[] _gamma;
    private readonly double[] _matrix;

    public PdfCalRgbColorSpace(double[] whitePoint, double[] gamma, double[] matrix)
    {
        _whitePoint = whitePoint;
        _gamma = gamma is { Length: 3 } ? gamma : [1, 1, 1];
        _matrix = matrix is { Length: 9 } ? matrix : [1, 0, 0, 0, 1, 0, 0, 0, 1];
    }

    public override string Family => PdfColorSpaceFamilies.CalRGB;

    public override int Components => 3;

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        var a = Math.Pow(Clamp01(At(components, 0)), _gamma[0]);
        var b = Math.Pow(Clamp01(At(components, 1)), _gamma[1]);
        var c = Math.Pow(Clamp01(At(components, 2)), _gamma[2]);
        var m = _matrix;
        PdfCie.XyzToSrgb(m[0] * a + m[3] * b + m[6] * c, m[1] * a + m[4] * b + m[7] * c,
            m[2] * a + m[5] * b + m[8] * c, _whitePoint, rgb);
    }
}
