using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Text;

namespace Adamantium.Pdf.Annotations;

/// <summary>Appearances a viewer draws for annotations the file gives none, from their entries (ISO 32000-2, 12.5.2
/// <c>Border</c> and <c>C</c>, 12.5.4): forms in the annotation's own box, set onto its rectangle as any appearance.</summary>
internal static class PdfImpliedAppearance
{
    private const double Kappa = 0.5523;

    // A text markup's line weight, and heights over the baseline side, as parts of the marked area's height.
    private const double MarkupLine = 0.07;
    private const double UnderlineAt = 0.08;
    private const double StrikeOutAt = 0.42;
    private const double SquiggleHeight = 0.1;
    private const double SquiggleStep = 0.2;
    private const int MaxSquiggleSteps = 16384;

    /// <summary>A link's border round its rectangle or each quadrilateral, in its color - beveled and inset ones with a
    /// two-tone band inside; null when it would show nothing.</summary>
    public static PdfStream LinkBorder(PdfRectangle rect, PdfAnnotationBorder border, IReadOnlyList<double> color,
        double[] quadPoints)
    {
        var width = rect.Width;
        var height = rect.Height;
        if (border.Width <= 0 || width <= 0 || height <= 0 || color is { Count: not (1 or 3 or 4) })
        {
            return null;
        }

        var content = new StringBuilder("q ");
        content.Append(StrokeColor(color ?? [0])).Append(' ');
        content.Append(Number(border.Width)).Append(" w ");
        if (border.Style == PdfBorderStyle.Dashed)
        {
            content.Append('[');
            foreach (var length in border.Dash)
            {
                content.Append(Number(length)).Append(' ');
            }

            content.Append("] 0 d ");
        }

        var half = border.Width / 2;
        if (quadPoints is { Length: >= 8 })
        {
            for (var start = 0; start + 8 <= quadPoints.Length; start += 8)
            {
                var corners = Inset(Corners(quadPoints, start, rect), half);
                Move(content, corners[0].X, corners[0].Y);
                Line(content, corners[1].X, corners[1].Y);
                if (border.Style != PdfBorderStyle.Underline)
                {
                    Line(content, corners[2].X, corners[2].Y);
                    Line(content, corners[3].X, corners[3].Y);
                    content.Append("h ");
                }
            }

            content.Append("S ");
        }
        else if (border.Style == PdfBorderStyle.Underline)
        {
            content.Append(Number(0)).Append(' ').Append(Number(half)).Append(" m ");
            content.Append(Number(width)).Append(' ').Append(Number(half)).Append(" l S ");
        }
        else
        {
            Box(content, half, half, width - border.Width, height - border.Width, Math.Max(0, border.CornerX - half),
                Math.Max(0, border.CornerY - half));
            content.Append(" S ");
        }

        if (border.Style is PdfBorderStyle.Beveled or PdfBorderStyle.Inset)
        {
            content.Append(Bevel(rect, border, quadPoints));
        }

        content.Append('Q');

        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Type, new PdfName(PdfKeys.XObject));
        dictionary.Set(PdfKeys.Subtype, new PdfName(PdfKeys.Form));
        dictionary.Set(PdfKeys.BBox, new PdfArray([new PdfReal(0), new PdfReal(0), new PdfReal(width), new PdfReal(height)]));
        return new PdfStream(dictionary, Encoding.ASCII.GetBytes(content.ToString()));
    }

    /// <summary>A text markup over each area, in its color (a highlight yellow, a line black, by default) and opacities:
    /// a highlight fills the area multiplied into the page; an underline, strikeout or squiggle runs along its baseline
    /// side. Null when it would show nothing.</summary>
    public static PdfStream TextMarkup(string subtype, PdfRectangle rect, IReadOnlyList<double> color,
        double strokeOpacity, double fillOpacity, IReadOnlyList<PdfQuad> quads)
    {
        if (quads.Count == 0 || rect.Width <= 0 || rect.Height <= 0 || color is { Count: not (1 or 3 or 4) })
        {
            return null;
        }

        var highlight = subtype == PdfAnnotationTypes.Highlight;
        var content = new StringBuilder("q /Markup gs ");
        content.Append(highlight ? FillColor(color ?? [1, 1, 0]) : StrokeColor(color ?? [0])).Append(' ');
        if (!highlight)
        {
            content.Append("0 J 0 j [] 0 d ");
        }

        var drawn = 0;
        var squiggleBudget = MaxSquiggleSteps;
        foreach (var quad in quads)
        {
            if (highlight)
            {
                // Round the middle, whatever order the file gives the corners in.
                var corners = Sorted([
                    Local(quad.BottomLeft, rect), Local(quad.BottomRight, rect), Local(quad.TopRight, rect),
                    Local(quad.TopLeft, rect),
                ]);
                if (Math.Abs(Area(corners)) < 1e-9)
                {
                    continue;
                }

                Move(content, corners[0].X, corners[0].Y);
                for (var corner = 1; corner < 4; corner++)
                {
                    Line(content, corners[corner].X, corners[corner].Y);
                }

                content.Append("h ");
                drawn++;
                continue;
            }

            var bottomLeft = Local(quad.BottomLeft, rect);
            var bottomRight = Local(quad.BottomRight, rect);
            var alongX = bottomRight.X - bottomLeft.X;
            var alongY = bottomRight.Y - bottomLeft.Y;
            var length = Math.Sqrt(alongX * alongX + alongY * alongY);
            if (length < 1e-9)
            {
                continue;
            }

            // Up is square to the baseline, as far as the top side stands over it.
            var height = ((quad.TopLeft.Y - quad.BottomLeft.Y) * alongX - (quad.TopLeft.X - quad.BottomLeft.X) * alongY) /
                         length;
            if (Math.Abs(height) < 1e-9)
            {
                continue;
            }

            var upX = -alongY / length * height;
            var upY = alongX / length * height;
            var width = Number(Math.Abs(height) * MarkupLine) + " w ";
            if (subtype == PdfAnnotationTypes.Squiggly)
            {
                if (Squiggle(content, width, bottomLeft, alongX / length, alongY / length, length, upX, upY, rect,
                        ref squiggleBudget))
                {
                    drawn++;
                }

                continue;
            }

            var at = subtype == PdfAnnotationTypes.StrikeOut ? StrikeOutAt : UnderlineAt;
            content.Append(width);
            Move(content, bottomLeft.X + upX * at, bottomLeft.Y + upY * at);
            Line(content, bottomRight.X + upX * at, bottomRight.Y + upY * at);
            content.Append("S ");
            drawn++;
        }

        if (drawn == 0)
        {
            return null;
        }

        if (highlight)
        {
            content.Append("f ");
        }

        content.Append('Q');

        var state = new PdfDictionary();
        state.Set(PdfKeys.Type, new PdfName(PdfKeys.ExtGState));
        state.Set(PdfKeys.StrokeOpacity, new PdfReal(strokeOpacity));
        state.Set(PdfKeys.FillOpacity, new PdfReal(fillOpacity));
        if (highlight)
        {
            state.Set(Graphics.PdfGraphicsKeys.BlendMode, new PdfName(nameof(Graphics.PdfBlendMode.Multiply)));
        }

        var states = new PdfDictionary();
        states.Set("Markup", state);
        var resources = new PdfDictionary();
        resources.Set(PdfKeys.ExtGState, states);

        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Type, new PdfName(PdfKeys.XObject));
        dictionary.Set(PdfKeys.Subtype, new PdfName(PdfKeys.Form));
        dictionary.Set(PdfKeys.BBox, new PdfArray([new PdfReal(0), new PdfReal(0), new PdfReal(rect.Width),
            new PdfReal(rect.Height)]));
        dictionary.Set(PdfKeys.Resources, resources);
        return new PdfStream(dictionary, Encoding.ASCII.GetBytes(content.ToString()));
    }

    // A zigzag along the baseline side, rising and falling a step of a fifth of the height at a time - only over the
    // part the annotation's box shows, in no more steps than the budget left. False when nothing of it shows.
    private static bool Squiggle(StringBuilder content, string width, PdfPoint from, double directionX,
        double directionY, double length, double upX, double upY, PdfRectangle rect, ref int budget)
    {
        var lowX = from.X + upX * MarkupLine;
        var lowY = from.Y + upY * MarkupLine;
        var first = length;
        var last = 0.0;
        foreach (var corner in (ReadOnlySpan<PdfPoint>)
                 [new(0, 0), new(rect.Width, 0), new(rect.Width, rect.Height), new(0, rect.Height)])
        {
            var along = (corner.X - lowX) * directionX + (corner.Y - lowY) * directionY;
            first = Math.Min(first, along);
            last = Math.Max(last, along);
        }

        first = Math.Max(first, 0);
        last = Math.Min(last, length);
        var step = Math.Max(0.5, Math.Sqrt(upX * upX + upY * upY) * SquiggleStep);
        if (!(last > first) || budget <= 0)
        {
            return false;
        }

        // Counted, not added up: far from the origin a step can be smaller than a double's own step there.
        var before = Math.Floor(first / step);
        var start = before * step;
        var wanted = Math.Ceiling((last - start) / step);
        var count = (int)Math.Clamp(wanted, 1, budget);
        if (count < wanted)
        {
            step = (last - start) / count;
        }

        budget -= count;
        var raised = before % 2 == 1;
        content.Append(width);
        Move(content, lowX + directionX * start + upX * (raised ? SquiggleHeight : 0),
            lowY + directionY * start + upY * (raised ? SquiggleHeight : 0));
        for (var index = 1; index <= count; index++)
        {
            var at = Math.Min(start + index * step, last);
            raised = !raised;
            var lift = raised ? SquiggleHeight : 0;
            Line(content, lowX + directionX * at + upX * lift, lowY + directionY * at + upY * lift);
        }

        content.Append("S ");
        return true;
    }

    private static PdfPoint Local(PdfPoint point, PdfRectangle rect) => new(point.X - rect.Left, point.Y - rect.Bottom);

    private static double Area(PdfPoint[] corners)
    {
        var twice = 0.0;
        for (var corner = 0; corner < corners.Length; corner++)
        {
            var next = corners[(corner + 1) % corners.Length];
            twice += corners[corner].X * next.Y - next.X * corners[corner].Y;
        }

        return twice / 2;
    }

    private static string FillColor(IReadOnlyList<double> color) => color.Count switch
    {
        1 => Number(color[0]) + " g",
        3 => Number(color[0]) + " " + Number(color[1]) + " " + Number(color[2]) + " rg",
        _ => Number(color[0]) + " " + Number(color[1]) + " " + Number(color[2]) + " " + Number(color[3]) + " k",
    };

    private static string Bevel(PdfRectangle rect, PdfAnnotationBorder border, double[] quadPoints)
    {
        var areas = new List<PdfPoint[]>();
        if (quadPoints is { Length: >= 8 })
        {
            for (var start = 0; start + 8 <= quadPoints.Length; start += 8)
            {
                areas.Add(Corners(quadPoints, start, rect));
            }
        }
        else
        {
            areas.Add([new PdfPoint(0, 0), new PdfPoint(rect.Width, 0), new PdfPoint(rect.Width, rect.Height),
                new PdfPoint(0, rect.Height)]);
        }

        var lit = new StringBuilder();
        var shaded = new StringBuilder();
        foreach (var area in areas)
        {
            var outer = Inset(area, border.Width);
            var inner = Inset(area, border.Width * 2);
            var top = 0;
            var left = 0;
            for (var side = 1; side < 4; side++)
            {
                if (Middle(area, side).Y > Middle(area, top).Y)
                {
                    top = side;
                }

                if (Middle(area, side).X < Middle(area, left).X)
                {
                    left = side;
                }
            }

            for (var side = 0; side < 4; side++)
            {
                var band = side == top || side == left ? lit : shaded;
                var next = (side + 1) % 4;
                Move(band, outer[side].X, outer[side].Y);
                Line(band, outer[next].X, outer[next].Y);
                Line(band, inner[next].X, inner[next].Y);
                Line(band, inner[side].X, inner[side].Y);
                band.Append("h ");
            }
        }

        var raised = border.Style == PdfBorderStyle.Beveled;
        return (raised ? "1" : "0.5") + " g " + lit + "f " + (raised ? "0.5" : "0.75") + " g " + shaded + "f ";
    }

    private static PdfPoint Middle(PdfPoint[] area, int side)
    {
        var next = area[(side + 1) % 4];
        return new PdfPoint((area[side].X + next.X) / 2, (area[side].Y + next.Y) / 2);
    }

    private static PdfPoint[] Corners(double[] points, int start, PdfRectangle rect)
    {
        var corners = new PdfPoint[4];
        for (var corner = 0; corner < 4; corner++)
        {
            corners[corner] = new PdfPoint(points[start + corner * 2] - rect.Left, points[start + corner * 2 + 1] - rect.Bottom);
        }

        return Sorted(corners);
    }

    // The corners in turn round their middle, counterclockwise from the left of the bottom.
    private static PdfPoint[] Sorted(PdfPoint[] corners)
    {
        var centerX = 0.0;
        var centerY = 0.0;
        for (var corner = 0; corner < 4; corner++)
        {
            centerX += corners[corner].X / 4;
            centerY += corners[corner].Y / 4;
        }

        var angles = new double[4];
        for (var corner = 0; corner < 4; corner++)
        {
            angles[corner] = Math.Atan2(corners[corner].Y - centerY, corners[corner].X - centerX);
        }

        Array.Sort(angles, corners);
        return corners;
    }

    private static PdfPoint[] Inset(PdfPoint[] corners, double by)
    {
        var inset = new PdfPoint[4];
        for (var corner = 0; corner < 4; corner++)
        {
            var here = corners[corner];
            var next = corners[(corner + 1) % 4];
            var previous = corners[(corner + 3) % 4];
            var nextX = next.X - here.X;
            var nextY = next.Y - here.Y;
            var previousX = previous.X - here.X;
            var previousY = previous.Y - here.Y;
            var nextLength = Math.Sqrt(nextX * nextX + nextY * nextY);
            var previousLength = Math.Sqrt(previousX * previousX + previousY * previousY);
            var alongNext = nextLength > 1e-9 ? Math.Min(by, nextLength / 2) / nextLength : 0;
            var alongPrevious = previousLength > 1e-9 ? Math.Min(by, previousLength / 2) / previousLength : 0;
            inset[corner] = new PdfPoint(here.X + nextX * alongNext + previousX * alongPrevious,
                here.Y + nextY * alongNext + previousY * alongPrevious);
        }

        return inset;
    }

    private static string StrokeColor(IReadOnlyList<double> color) => color.Count switch
    {
        1 => Number(color[0]) + " G",
        3 => Number(color[0]) + " " + Number(color[1]) + " " + Number(color[2]) + " RG",
        _ => Number(color[0]) + " " + Number(color[1]) + " " + Number(color[2]) + " " + Number(color[3]) + " K",
    };

    private static void Box(StringBuilder content, double left, double bottom, double width, double height,
        double cornerX, double cornerY)
    {
        var radiusX = Math.Min(cornerX, Math.Max(0, width) / 2);
        var radiusY = Math.Min(cornerY, Math.Max(0, height) / 2);
        if (radiusX <= 0 || radiusY <= 0)
        {
            content.Append(Number(left)).Append(' ').Append(Number(bottom)).Append(' ');
            content.Append(Number(Math.Max(0, width))).Append(' ').Append(Number(Math.Max(0, height))).Append(" re");
            return;
        }

        var right = left + width;
        var top = bottom + height;
        var pullX = radiusX * Kappa;
        var pullY = radiusY * Kappa;
        Move(content, left + radiusX, bottom);
        Line(content, right - radiusX, bottom);
        Curve(content, right - radiusX + pullX, bottom, right, bottom + radiusY - pullY, right, bottom + radiusY);
        Line(content, right, top - radiusY);
        Curve(content, right, top - radiusY + pullY, right - radiusX + pullX, top, right - radiusX, top);
        Line(content, left + radiusX, top);
        Curve(content, left + radiusX - pullX, top, left, top - radiusY + pullY, left, top - radiusY);
        Line(content, left, bottom + radiusY);
        Curve(content, left, bottom + radiusY - pullY, left + radiusX - pullX, bottom, left + radiusX, bottom);
        content.Append("h");
    }

    private static void Move(StringBuilder content, double atX, double atY) =>
        content.Append(Number(atX)).Append(' ').Append(Number(atY)).Append(" m ");

    private static void Line(StringBuilder content, double toX, double toY) =>
        content.Append(Number(toX)).Append(' ').Append(Number(toY)).Append(" l ");

    private static void Curve(StringBuilder content, double firstX, double firstY, double secondX, double secondY,
        double toX, double toY) =>
        content.Append(Number(firstX)).Append(' ').Append(Number(firstY)).Append(' ').Append(Number(secondX)).Append(' ')
            .Append(Number(secondY)).Append(' ').Append(Number(toX)).Append(' ').Append(Number(toY)).Append(" c ");

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
