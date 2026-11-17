using System;
using System.Collections.Generic;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Annotations;

/// <summary>An annotation's border (ISO 32000-2, 12.5.2 <c>Border</c> and 12.5.4 <c>BS</c>, the latter winning): its
/// width in points, style, dash and corner radii.</summary>
public sealed class PdfAnnotationBorder
{
    private static readonly double[] DefaultDash = [3];

    private PdfAnnotationBorder(double width, PdfBorderStyle style, IReadOnlyList<double> dash, double cornerX,
        double cornerY)
    {
        Width = width;
        Style = style;
        Dash = dash;
        CornerX = cornerX;
        CornerY = cornerY;
    }

    /// <summary>The width in points; 0 draws no border.</summary>
    public double Width { get; }

    public PdfBorderStyle Style { get; }

    /// <summary>The dash and gap lengths of a <see cref="PdfBorderStyle.Dashed"/> border; empty for a solid one.</summary>
    public IReadOnlyList<double> Dash { get; }

    /// <summary>The horizontal corner radius, from the older <c>Border</c> array; 0 for square corners.</summary>
    public double CornerX { get; }

    public double CornerY { get; }

    internal static PdfAnnotationBorder From(PdfDictionary annotation, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(annotation[PdfAnnotationKeys.Border]) is PdfDictionary style)
        {
            var width = Math.Max(0, PdfValues.Number(style[PdfAnnotationKeys.BorderWidth], resolver, 1));
            var kind = PdfValues.Name(style[PdfAnnotationKeys.BorderStyle], resolver) switch
            {
                "D" => PdfBorderStyle.Dashed,
                "B" => PdfBorderStyle.Beveled,
                "I" => PdfBorderStyle.Inset,
                "U" => PdfBorderStyle.Underline,
                _ => PdfBorderStyle.Solid,
            };
            var dash = kind == PdfBorderStyle.Dashed ? DashOf(PdfValues.Numbers(style[PdfAnnotationKeys.Dash], resolver)) : [];
            return new PdfAnnotationBorder(width, kind, dash, 0, 0);
        }

        if (resolver.Resolve(annotation[PdfAnnotationKeys.OldBorder]) is PdfArray array && array.Count >= 3)
        {
            var cornerX = Math.Max(0, PdfValues.Number(array[0], resolver));
            var cornerY = Math.Max(0, PdfValues.Number(array[1], resolver));
            var width = Math.Max(0, PdfValues.Number(array[2], resolver));
            IReadOnlyList<double> dash = array.Count > 3 && Valid(PdfValues.Numbers(array[3], resolver)) is { } given
                ? given
                : [];
            var kind = dash.Count > 0 ? PdfBorderStyle.Dashed : PdfBorderStyle.Solid;
            return new PdfAnnotationBorder(width, kind, dash, cornerX, cornerY);
        }

        return new PdfAnnotationBorder(1, PdfBorderStyle.Solid, [], 0, 0);
    }

    private static IReadOnlyList<double> DashOf(double[] numbers) => Valid(numbers) ?? DefaultDash;

    private static double[] Valid(double[] numbers)
    {
        if (numbers == null || numbers.Length == 0)
        {
            return null;
        }

        var total = 0.0;
        foreach (var length in numbers)
        {
            if (length < 0)
            {
                return null;
            }

            total += length;
        }

        return total > 0 ? numbers : null;
    }
}
