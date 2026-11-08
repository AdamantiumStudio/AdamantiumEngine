using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>Lab: CIE 1976 L*a*b*, L* from 0 to 100, a* and b* within <c>/Range</c>.</summary>
public sealed class PdfLabColorSpace : PdfColorSpace
{
    private readonly double[] _whitePoint;
    private readonly double[] _range;

    public PdfLabColorSpace(double[] whitePoint, double[] range)
    {
        _whitePoint = whitePoint;
        range = range is { Length: 4 } ? range : [-100, 100, -100, 100];
        _range = [Math.Min(range[0], range[1]), Math.Max(range[0], range[1]), Math.Min(range[2], range[3]),
            Math.Max(range[2], range[3])];
    }

    public override string Family => "Lab";

    public override int Components => 3;

    public override double[] DefaultDecode(int bitsPerComponent) => [0, 100, _range[0], _range[1], _range[2], _range[3]];

    public override void InitialColor(Span<double> components)
    {
        components.Clear();
        components[1] = Math.Clamp(0, _range[0], _range[1]);
        components[2] = Math.Clamp(0, _range[2], _range[3]);
    }

    public override void ToRgb(ReadOnlySpan<double> components, Span<double> rgb)
    {
        var l = Math.Clamp(At(components, 0), 0, 100);
        var a = Math.Clamp(At(components, 1), _range[0], _range[1]);
        var b = Math.Clamp(At(components, 2), _range[2], _range[3]);
        var m = (l + 16) / 116;
        var x = _whitePoint[0] * Inverse(m + a / 500);
        var y = _whitePoint[1] * Inverse(m);
        var z = _whitePoint[2] * Inverse(m - b / 200);
        PdfCie.XyzToSrgb(x, y, z, _whitePoint, rgb);
    }

    private static double Inverse(double value) =>
        value >= 6.0 / 29 ? value * value * value : 108.0 / 841 * (value - 4.0 / 29);
}
