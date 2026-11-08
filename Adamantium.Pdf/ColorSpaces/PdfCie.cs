using System;

namespace Adamantium.Pdf.ColorSpaces;

/// <summary>CIE XYZ to sRGB: the white point adapted to D65 by the Bradford transform, then IEC 61966-2-1's matrix and
/// transfer curve.</summary>
internal static class PdfCie
{
    private const double D65X = 0.95047;
    private const double D65Z = 1.08883;

    public static void XyzToSrgb(double x, double y, double z, ReadOnlySpan<double> whitePoint, Span<double> rgb)
    {
        var wx = whitePoint.Length > 0 && whitePoint[0] > 0 ? whitePoint[0] : D65X;
        var wy = whitePoint.Length > 1 && whitePoint[1] > 0 ? whitePoint[1] : 1;
        var wz = whitePoint.Length > 2 && whitePoint[2] > 0 ? whitePoint[2] : D65Z;
        x /= wy;
        y /= wy;
        z /= wy;
        wx /= wy;
        wz /= wy;

        var (sl, sm, ss) = Bradford(wx, 1, wz);
        var (dl, dm, ds) = Bradford(D65X, 1, D65Z);
        var (l, m, s) = Bradford(x, y, z);
        l *= dl / sl;
        m *= dm / sm;
        s *= ds / ss;
        var ax = 0.9869929 * l - 0.1470543 * m + 0.1599627 * s;
        var ay = 0.4323053 * l + 0.5183603 * m + 0.0492912 * s;
        var az = -0.0085287 * l + 0.0400428 * m + 0.9684867 * s;

        rgb[0] = Encode(3.2404542 * ax - 1.5371385 * ay - 0.4985314 * az);
        rgb[1] = Encode(-0.9692660 * ax + 1.8760108 * ay + 0.0415560 * az);
        rgb[2] = Encode(0.0556434 * ax - 0.2040259 * ay + 1.0572252 * az);
    }

    private static (double L, double M, double S) Bradford(double x, double y, double z) =>
        (0.8951 * x + 0.2664 * y - 0.1614 * z,
            -0.7502 * x + 1.7135 * y + 0.0367 * z,
            0.0389 * x - 0.0685 * y + 1.0296 * z);

    private static double Encode(double linear)
    {
        linear = linear < 0 ? 0 : linear > 1 ? 1 : linear;
        return linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
    }
}
