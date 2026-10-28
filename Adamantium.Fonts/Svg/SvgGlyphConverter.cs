using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Tables;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Svg;

internal sealed class SvgGlyphConverter
{
    private const int MaxDepth = 32;
    private const int MaxReferences = 8;
    private const int MaxVisits = 4096;
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private static readonly Matrix3x2<double> Flip = new(1, 0, 0, -1, 0, 0);

    private static readonly HashSet<string> Skipped =
    [
        "defs", "clipPath", "linearGradient", "radialGradient", "mask", "pattern", "style", "title", "desc", "metadata",
        "symbol", "marker", "filter",
    ];

    private readonly SvgDocument document;
    private readonly ColorPaletteTable palettes;
    private readonly int palette;
    private readonly Func<string, List<List<OutlinePoint>>, uint> outline;
    private readonly uint glyph;
    private readonly List<ColorPaintOperation> operations = [];
    private int ordinal;
    private int visits;
    private int boxVisits;

    private SvgGlyphConverter(SvgDocument document, uint glyph, ColorPaletteTable palettes, int palette,
        Func<string, List<List<OutlinePoint>>, uint> outline)
    {
        this.document = document;
        this.glyph = glyph;
        this.palettes = palettes;
        this.palette = palette;
        this.outline = outline;
    }

    public static ColorPaintOperation[] Convert(SvgDocument document, uint glyph, ColorPaletteTable palettes, int palette,
        Func<string, List<List<OutlinePoint>>, uint> outline)
    {
        var element = document?.Find("glyph" + glyph.ToString(CultureInfo.InvariantCulture));
        if (element == null)
        {
            return [];
        }

        // The glyph's ancestors style and move it, hide it, and clip it, the outermost first.
        var converter = new SvgGlyphConverter(document, glyph, palettes, palette, outline);
        var ctm = Matrix3x2<double>.Identity;
        var style = SvgStyle.Initial;
        var clips = 0;
        foreach (var ancestor in element.Ancestors().Reverse())
        {
            if (SvgStyle.Property(ancestor, "display") == "none")
            {
                return [];
            }

            style = style.Inherit(ancestor, palettes, palette, true);
            ctm = Matrix3x2<double>.Multiply(SvgTransform.Parse((string)ancestor.Attribute("transform")), ctm);
            clips += converter.PushClipPath(ancestor, ctm) ? 1 : 0;
        }

        converter.Walk(element, ctm, style, 0, false);
        for (var k = 0; k < clips; k++)
        {
            converter.operations.Add(ColorPaintOperation.PopClip());
        }

        return converter.operations.Count > 2 * clips ? converter.operations.ToArray() : [];
    }

    private void Walk(XElement element, Matrix3x2<double> parent, SvgStyle inherited, int depth, bool referenced)
    {
        var name = element.Name.LocalName;
        if (depth > MaxDepth || ++visits > MaxVisits || (Skipped.Contains(name) && !(referenced && name == "symbol")) ||
            SvgStyle.Property(element, "display") == "none")
        {
            return;
        }

        var isShape = name is "path" or "rect" or "circle" or "ellipse" or "polygon" or "polyline";
        var opacity = SvgStyle.Fraction(SvgStyle.Property(element, "opacity")) ?? 1;
        var grouped = !isShape && opacity < 1;
        var style = inherited.Inherit(element, palettes, palette, !grouped);
        var transform = SvgTransform.Parse((string)element.Attribute("transform"));
        if (name == "use")
        {
            transform = Matrix3x2<double>.Multiply(new Matrix3x2<double>(1, 0, 0, 1, Number(element, "x"), Number(element, "y")), transform);
        }

        var ctm = Matrix3x2<double>.Multiply(transform, parent);
        var clipped = PushClipPath(element, ctm);
        if (grouped)
        {
            operations.Add(ColorPaintOperation.PushGroup());
        }

        if (name == "use")
        {
            var target = document.FindReference(Reference(element));
            if (target != null)
            {
                Walk(target, ctm, style, depth + 1, true);
            }
        }
        else if (isShape)
        {
            Shape(element, ctm, style);
        }
        else
        {
            foreach (var child in element.Elements())
            {
                Walk(child, ctm, style, depth + 1, false);
            }
        }

        if (grouped)
        {
            operations.Add(ColorPaintOperation.PushGroup());
            operations.Add(ColorPaintOperation.FillWith(Solid(Stop(0, Color.FromRgba(0, 0, 0, 255), opacity))));
            operations.Add(ColorPaintOperation.PopGroup(ColorCompositeMode.DestinationIn));
            operations.Add(ColorPaintOperation.PopGroup(ColorCompositeMode.SourceOver));
        }

        if (clipped)
        {
            operations.Add(ColorPaintOperation.PopClip());
        }
    }

    private void Shape(XElement element, Matrix3x2<double> ctm, SvgStyle style)
    {
        if (style.Fill.Kind == SvgPaintKind.None)
        {
            return;
        }

        var contours = SvgOutlines.Of(element);
        if (contours.Count == 0)
        {
            return;
        }

        var fill = Fill(style, Bounds(contours), ctm);
        if (fill == null)
        {
            return;
        }

        operations.Add(ColorPaintOperation.PushClip(Outline(contours, ctm, style.EvenOdd), Matrix3x2<double>.Identity));
        operations.Add(ColorPaintOperation.FillWith(fill));
        operations.Add(ColorPaintOperation.PopClip());
    }

    private bool PushClipPath(XElement element, Matrix3x2<double> ctm)
    {
        var clipPath = document.FindReference(SvgStyle.Property(element, "clip-path"));
        if (clipPath == null || clipPath.Name.LocalName != "clipPath")
        {
            return false;
        }

        // In the units of the box of what it clips, that box mapped from 0 to 1 across and down, then moved by the clip's
        // own transform, as browsers place it.
        var units = Matrix3x2<double>.Identity;
        if ((string)clipPath.Attribute("clipPathUnits") == "objectBoundingBox")
        {
            boxVisits = 0;
            if (BoxOf(element, Matrix3x2<double>.Identity, 0, true, false) is not { Width: > 0, Height: > 0 } box)
            {
                operations.Add(ColorPaintOperation.PushClip(Register([], false), Matrix3x2<double>.Identity));
                return true;
            }

            units = new Matrix3x2<double>(box.Width, 0, 0, box.Height, box.X, box.Y);
        }

        var clipCtm = Matrix3x2<double>.Multiply(Matrix3x2<double>.Multiply(units,
            SvgTransform.Parse((string)clipPath.Attribute("transform"))), ctm);
        var contours = new List<List<OutlinePoint>>();
        var evenOdd = false;
        var clipRule = SvgStyle.Property(clipPath, "clip-rule");
        foreach (var child in clipPath.Elements())
        {
            var shape = child;
            var childCtm = Matrix3x2<double>.Multiply(SvgTransform.Parse((string)child.Attribute("transform")), clipCtm);
            for (var hops = 0; hops < MaxReferences && shape.Name.LocalName == "use"; hops++)
            {
                childCtm = Matrix3x2<double>.Multiply(new Matrix3x2<double>(1, 0, 0, 1, Number(shape, "x"), Number(shape, "y")), childCtm);
                var target = document.FindReference(Reference(shape));
                if (target == null)
                {
                    break;
                }

                shape = target;
                childCtm = Matrix3x2<double>.Multiply(SvgTransform.Parse((string)shape.Attribute("transform")), childCtm);
            }

            var rule = SvgStyle.Property(child, "clip-rule");
            evenOdd |= (rule == null || rule == "inherit" ? clipRule : rule) == "evenodd";
            foreach (var contour in SvgOutlines.Of(shape))
            {
                contours.Add(Transform(contour, childCtm));
            }
        }

        operations.Add(ColorPaintOperation.PushClip(Register(contours, evenOdd), Matrix3x2<double>.Identity));
        return true;
    }

    // An element's box in its own units, as SVG's objectBoundingBox takes it: its shape's, or the union of what it draws,
    // placed as the walk places it - the element's own transform and a use's place are already in its units at the top;
    // the geometry alone, null for none.
    private RectangleF? BoxOf(XElement element, Matrix3x2<double> parent, int depth, bool top, bool referenced)
    {
        var name = element.Name.LocalName;
        if (depth > MaxDepth || ++boxVisits > MaxVisits || (Skipped.Contains(name) && !(referenced && name == "symbol"))
            || SvgStyle.Property(element, "display") == "none")
        {
            return null;
        }

        var own = top ? Matrix3x2<double>.Identity : SvgTransform.Parse((string)element.Attribute("transform"));
        if (!top && name == "use")
        {
            own = Matrix3x2<double>.Multiply(new Matrix3x2<double>(1, 0, 0, 1, Number(element, "x"), Number(element, "y")), own);
        }

        var transform = Matrix3x2<double>.Multiply(own, parent);
        if (name is "path" or "rect" or "circle" or "ellipse" or "polygon" or "polyline")
        {
            return Bounds(SvgOutlines.Of(element).Select(contour => contour
                .Select(p => new OutlinePoint(Vector2<double>.Transform((Vector2<double>)p, transform), p.IsControl)).ToList()).ToList());
        }

        RectangleF? box = null;
        IEnumerable<(XElement Child, bool Referenced)> children = name == "use"
            ? document.FindReference(Reference(element)) is { } target ? [(target, true)] : []
            : element.Elements().Select(child => (child, false));
        foreach (var (child, isTarget) in children)
        {
            if (BoxOf(child, transform, depth + 1, false, isTarget) is not { } part)
            {
                continue;
            }

            if (box is { } held)
            {
                var left = Math.Min(held.Left, part.Left);
                var upper = Math.Min(held.Top, part.Top);
                box = new RectangleF(left, upper, Math.Max(held.Right, part.Right) - left,
                    Math.Max(held.Bottom, part.Bottom) - upper);
            }
            else
            {
                box = part;
            }
        }

        return box;
    }

    private uint Outline(List<List<OutlinePoint>> contours, Matrix3x2<double> ctm, bool evenOdd)
    {
        return Register(contours.Select(c => Transform(c, ctm)).ToList(), evenOdd);
    }

    private uint Register(List<List<OutlinePoint>> contours, bool evenOdd)
    {
        if (evenOdd)
        {
            OrientForEvenOdd(contours);
        }

        return outline(glyph.ToString(CultureInfo.InvariantCulture) + ":" + ordinal++, contours);
    }

    private static List<OutlinePoint> Transform(List<OutlinePoint> contour, Matrix3x2<double> ctm)
    {
        var toFont = Matrix3x2<double>.Multiply(ctm, Flip);
        return contour.Select(p => new OutlinePoint(Vector2<double>.Transform((Vector2<double>)p, toFont), p.IsControl)).ToList();
    }

    private ColorFill Fill(SvgStyle style, RectangleF? bounds, Matrix3x2<double> ctm)
    {
        var alpha = style.FillOpacity * style.Opacity;
        switch (style.Fill.Kind)
        {
            case SvgPaintKind.Color:
                return Solid(Stop(0, style.Fill.Color, alpha));
            case SvgPaintKind.CurrentColor:
                return Solid(CurrentColorStop(0, style, alpha));
            case SvgPaintKind.Reference:
                var gradient = document.FindReference(style.Fill.Reference);
                if (gradient != null && gradient.Name.LocalName is "linearGradient" or "radialGradient")
                {
                    return Gradient(gradient, style, bounds, ctm, alpha);
                }

                var fallback = SvgPaint.Parse(style.Fill.Fallback, palettes, palette);
                return fallback.Kind == SvgPaintKind.Color ? Solid(Stop(0, fallback.Color, alpha)) : null;
            default:
                return null;
        }
    }

    private static ColorFill Solid(ColorStop stop) =>
        new(ColorFillKind.Solid, [stop], ColorExtend.Pad, Matrix3x2<double>.Identity);

    private ColorFill Gradient(XElement gradient, SvgStyle style, RectangleF? bounds, Matrix3x2<double> ctm, double alpha)
    {
        var chain = new List<XElement> { gradient };
        for (var hops = 0; hops < MaxReferences; hops++)
        {
            var next = document.FindReference(Reference(chain[chain.Count - 1]));
            if (next == null || chain.Contains(next) || next.Name.LocalName is not ("linearGradient" or "radialGradient"))
            {
                break;
            }

            chain.Add(next);
        }

        string Attribute(string name) => chain.Select(e => (string)e.Attribute(name)).FirstOrDefault(v => v != null);

        var stops = Stops(chain.FirstOrDefault(e => e.Elements().Any(c => c.Name.LocalName == "stop")), style, alpha);
        if (stops.Count == 0)
        {
            return null;
        }

        if (stops.Count == 1)
        {
            return Solid(stops[0]);
        }

        var boxUnits = Attribute("gradientUnits") != "userSpaceOnUse";
        if (boxUnits && (bounds is not { } box || box.Width <= 0 || box.Height <= 0))
        {
            return null;
        }

        var units = boxUnits ? new Matrix3x2<double>(bounds.Value.Width, 0, 0, bounds.Value.Height, bounds.Value.X, bounds.Value.Y)
            : Matrix3x2<double>.Identity;
        var transform = Matrix3x2<double>.Multiply(Matrix3x2<double>.Multiply(Matrix3x2<double>.Multiply(
            SvgTransform.Parse(Attribute("gradientTransform")), units), ctm), Flip);
        var extend = Attribute("spreadMethod") switch
        {
            "reflect" => ColorExtend.Reflect,
            "repeat" => ColorExtend.Repeat,
            _ => ColorExtend.Pad,
        };

        double Coordinate(string name, double fallback) => Length(Attribute(name)) ?? fallback;

        if (gradient.Name.LocalName == "linearGradient")
        {
            var x1 = Coordinate("x1", 0);
            var y1 = Coordinate("y1", 0);
            var x2 = Coordinate("x2", 1);
            var y2 = Coordinate("y2", 0);
            return new ColorFill(ColorFillKind.LinearGradient, stops, extend, transform)
            {
                Point0 = new Vector2<double>(x1, y1),
                Point1 = new Vector2<double>(x2, y2),
                Point2 = new Vector2<double>(x1 - (y2 - y1), y1 + (x2 - x1)),
            };
        }

        var cx = Coordinate("cx", 0.5);
        var cy = Coordinate("cy", 0.5);
        return new ColorFill(ColorFillKind.RadialGradient, stops, extend, transform)
        {
            Point0 = new Vector2<double>(Coordinate("fx", cx), Coordinate("fy", cy)),
            Radius0 = Coordinate("fr", 0),
            Point1 = new Vector2<double>(cx, cy),
            Radius1 = Coordinate("r", 0.5),
        };
    }

    private List<ColorStop> Stops(XElement gradient, SvgStyle style, double alpha)
    {
        var stops = new List<ColorStop>();
        if (gradient == null)
        {
            return stops;
        }

        var last = 0.0;
        foreach (var stop in gradient.Elements().Where(e => e.Name.LocalName == "stop"))
        {
            var offset = Math.Max(last, SvgStyle.Fraction(((string)stop.Attribute("offset"))?.Trim()) ?? 0);
            last = offset;
            var opacity = (SvgStyle.Fraction(SvgStyle.Property(stop, "stop-opacity")) ?? 1) * alpha;
            var text = SvgStyle.Property(stop, "stop-color");
            if (text == "currentColor")
            {
                stops.Add(CurrentColorStop((float)offset, style, opacity));
                continue;
            }

            var color = text != null && SvgPaint.TryParseColor(text, palettes, palette, out var parsed) ? parsed : Colors.Black;
            stops.Add(Stop((float)offset, color, opacity));
        }

        return stops;
    }

    private static ColorStop Stop(float offset, Color color, double alpha)
    {
        color.A = (byte)Math.Max(0, Math.Min(255, Math.Round(color.A * alpha)));
        return new ColorStop(offset, color, (float)alpha);
    }

    private static ColorStop CurrentColorStop(float offset, SvgStyle style, double alpha)
    {
        return style.CurrentColor is { } color ? Stop(offset, color, alpha) : new ColorStop(offset, null, (float)alpha);
    }

    private static RectangleF? Bounds(List<List<OutlinePoint>> contours)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Add(Vector2<double> p)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        foreach (var contour in contours)
        {
            for (var i = 0; i < contour.Count; i++)
            {
                if (!contour[i].IsControl)
                {
                    Add(contour[i]);
                    continue;
                }

                if (i + 2 < contour.Count && i > 0)
                {
                    Vector2<double> p0 = contour[i - 1], p1 = contour[i], p2 = contour[i + 1], p3 = contour[i + 2];
                    foreach (var t in Extrema(p0.X, p1.X, p2.X, p3.X).Concat(Extrema(p0.Y, p1.Y, p2.Y, p3.Y)))
                    {
                        var u = 1 - t;
                        Add(p0 * (u * u * u) + p1 * (3 * u * u * t) + p2 * (3 * u * t * t) + p3 * (t * t * t));
                    }

                    i++;
                }
            }
        }

        return minX > maxX ? null : new RectangleF((float)minX, (float)minY, (float)(maxX - minX), (float)(maxY - minY));
    }

    private static IEnumerable<double> Extrema(double p0, double p1, double p2, double p3)
    {
        var a = -p0 + 3 * p1 - 3 * p2 + p3;
        var b = 2 * (p0 - 2 * p1 + p2);
        var c = p1 - p0;
        if (Math.Abs(a) < 1e-12)
        {
            if (Math.Abs(b) > 1e-12 && -c / b is > 0 and < 1)
            {
                yield return -c / b;
            }

            yield break;
        }

        var discriminant = b * b - 4 * a * c;
        if (discriminant < 0)
        {
            yield break;
        }

        foreach (var root in new[] { (-b + Math.Sqrt(discriminant)) / (2 * a), (-b - Math.Sqrt(discriminant)) / (2 * a) })
        {
            if (root is > 0 and < 1)
            {
                yield return root;
            }
        }
    }

    private static void OrientForEvenOdd(List<List<OutlinePoint>> contours)
    {
        for (var i = 0; i < contours.Count; i++)
        {
            var depth = 0;
            for (var j = 0; j < contours.Count; j++)
            {
                if (i != j && Contains(contours[j], contours[i][0]))
                {
                    depth++;
                }
            }

            if (SignedArea(contours[i]) > 0 != (depth % 2 == 0))
            {
                contours[i].Reverse();
            }
        }
    }

    private static bool Contains(List<OutlinePoint> polygon, OutlinePoint point)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if (a.Y > point.Y != b.Y > point.Y &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double SignedArea(List<OutlinePoint> contour)
    {
        var area = 0.0;
        for (int i = 0, j = contour.Count - 1; i < contour.Count; j = i++)
        {
            area += contour[j].X * contour[i].Y - contour[i].X * contour[j].Y;
        }

        return area;
    }

    private static string Reference(XElement element) =>
        (string)element.Attribute("href") ?? (string)element.Attribute(XLink + "href");

    private static double Number(XElement element, string name) => Length((string)element.Attribute(name)) ?? 0;

    private static double? Length(string text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var percent = text.EndsWith("%", StringComparison.Ordinal);
        if (text.EndsWith("px", StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - 2);
        }

        return double.TryParse(percent ? text.Substring(0, text.Length - 1) : text, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value)
            ? percent ? value / 100 : value
            : null;
    }
}
