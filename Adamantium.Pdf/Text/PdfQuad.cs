using System;
using Adamantium.Pdf.Document;

namespace Adamantium.Pdf.Text;

/// <summary>Four corners on a page, in default user space: a glyph's or a run of text's box as its line lies - turned
/// with rotated text. The corners go round from the start of the baseline's side.</summary>
public readonly record struct PdfQuad(PdfPoint BottomLeft, PdfPoint BottomRight, PdfPoint TopRight, PdfPoint TopLeft)
{
    /// <summary>The smallest upright rectangle holding the corners.</summary>
    public PdfRectangle Bounds => new(
        Math.Min(Math.Min(BottomLeft.X, BottomRight.X), Math.Min(TopRight.X, TopLeft.X)),
        Math.Min(Math.Min(BottomLeft.Y, BottomRight.Y), Math.Min(TopRight.Y, TopLeft.Y)),
        Math.Max(Math.Max(BottomLeft.X, BottomRight.X), Math.Max(TopRight.X, TopLeft.X)),
        Math.Max(Math.Max(BottomLeft.Y, BottomRight.Y), Math.Max(TopRight.Y, TopLeft.Y)));

    public PdfPoint Center => new((BottomLeft.X + TopRight.X) / 2, (BottomLeft.Y + TopRight.Y) / 2);

    /// <summary>Whether <paramref name="point"/> is inside, edges included - whatever order the corners were given in,
    /// as files write a link's quadrilaterals one way and a markup's another. A quadrilateral flattened to a line or a
    /// point holds nothing.</summary>
    public bool Contains(PdfPoint point)
    {
        Span<PdfPoint> corners = [BottomLeft, BottomRight, TopRight, TopLeft];
        var center = new PdfPoint((corners[0].X + corners[1].X + corners[2].X + corners[3].X) / 4,
            (corners[0].Y + corners[1].Y + corners[2].Y + corners[3].Y) / 4);
        Span<double> angles = stackalloc double[4];
        for (var i = 0; i < 4; i++)
        {
            angles[i] = Math.Atan2(corners[i].Y - center.Y, corners[i].X - center.X);
        }

        angles.Sort(corners);
        var area = 0.0;
        for (var i = 0; i < 4; i++)
        {
            var (a, b) = (corners[i], corners[(i + 1) % 4]);
            area += a.X * b.Y - b.X * a.Y;
        }

        var scale = Bounds.Width + Bounds.Height;
        if (!(Math.Abs(area) > 1e-9 * Math.Max(1, scale * scale)))
        {
            return false;
        }

        for (var i = 0; i < 4; i++)
        {
            var (a, b) = (corners[i], corners[(i + 1) % 4]);
            var cross = (b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X);
            if (cross < -1e-9 * Math.Max(1, scale))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The eight numbers of a text markup annotation's <c>QuadPoints</c> (ISO 32000-2, table 182): top left,
    /// top right, bottom left, bottom right, as readers expect them.</summary>
    public double[] ToQuadPoints() =>
    [
        TopLeft.X, TopLeft.Y, TopRight.X, TopRight.Y, BottomLeft.X, BottomLeft.Y, BottomRight.X, BottomRight.Y,
    ];
}
