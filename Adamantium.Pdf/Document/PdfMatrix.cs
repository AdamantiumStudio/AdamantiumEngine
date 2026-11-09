using System;

namespace Adamantium.Pdf.Document;

/// <summary>A transformation matrix as PDF writes it (ISO 32000-2, 8.3.3): [a b c d e f], points as rows,
/// x' = a x + c y + e and y' = b x + d y + f.</summary>
public readonly record struct PdfMatrix(double A, double B, double C, double D, double E, double F)
{
    public static PdfMatrix Identity { get; } = new(1, 0, 0, 1, 0, 0);

    /// <summary>The matrix of six numbers; identity when there are not six, or one is not finite.</summary>
    public static PdfMatrix From(double[] values)
    {
        if (values is not { Length: 6 })
        {
            return Identity;
        }

        foreach (var value in values)
        {
            if (!double.IsFinite(value))
            {
                return Identity;
            }
        }

        return new PdfMatrix(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    /// <summary>This transformation, then <paramref name="then"/>.</summary>
    public PdfMatrix Then(PdfMatrix then) => new(
        A * then.A + B * then.C,
        A * then.B + B * then.D,
        C * then.A + D * then.C,
        C * then.B + D * then.D,
        E * then.A + F * then.C + then.E,
        E * then.B + F * then.D + then.F);

    public PdfPoint Transform(double x, double y) => new(A * x + C * y + E, B * x + D * y + F);

    public PdfPoint Transform(PdfPoint point) => Transform(point.X, point.Y);

    /// <summary>The inverse; null when the matrix squashes the plane flat.</summary>
    public PdfMatrix? Inverse()
    {
        var determinant = A * D - B * C;
        if (Math.Abs(determinant) < 1e-12 || !double.IsFinite(determinant))
        {
            return null;
        }

        return new PdfMatrix(D / determinant, -B / determinant, -C / determinant, A / determinant,
            (C * F - D * E) / determinant, (B * E - A * F) / determinant);
    }
}
