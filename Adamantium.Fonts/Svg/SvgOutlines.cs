using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Adamantium.Fonts.Common;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Svg;

namespace Adamantium.Fonts.Svg;

internal static class SvgOutlines
{
    public static List<List<OutlinePoint>> Of(XElement element)
    {
        var sink = new SvgOutlineSink();
        switch (element.Name.LocalName)
        {
            case "path":
                SvgPathData.Walk((string)element.Attribute("d"), sink);
                break;
            case "rect":
                Rect(sink, Number(element, "x"), Number(element, "y"), Number(element, "width"), Number(element, "height"),
                    Optional(element, "rx"), Optional(element, "ry"));
                break;
            case "circle":
                var r = Number(element, "r");
                Ellipse(sink, Number(element, "cx"), Number(element, "cy"), r, r);
                break;
            case "ellipse":
                Ellipse(sink, Number(element, "cx"), Number(element, "cy"), Number(element, "rx"), Number(element, "ry"));
                break;
            case "polygon":
            case "polyline":
                Polygon(sink, SvgPathData.ReadNumbers((string)element.Attribute("points")));
                break;
        }

        return sink.Finish();
    }

    private static void Rect(SvgOutlineSink sink, double x, double y, double width, double height, double? rx, double? ry)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var radiusX = Math.Min(Math.Max(0, rx ?? ry ?? 0), width / 2);
        var radiusY = Math.Min(Math.Max(0, ry ?? rx ?? 0), height / 2);
        var right = x + width;
        var bottom = y + height;
        if (radiusX <= 0 || radiusY <= 0)
        {
            sink.MoveTo(new Vector2<double>(x, y));
            sink.LineTo(new Vector2<double>(right, y));
            sink.LineTo(new Vector2<double>(right, bottom));
            sink.LineTo(new Vector2<double>(x, bottom));
            sink.Close();
            return;
        }

        sink.MoveTo(new Vector2<double>(x + radiusX, y));
        sink.LineTo(new Vector2<double>(right - radiusX, y));
        sink.ArcTo(radiusX, radiusY, 0, false, true, new Vector2<double>(right, y + radiusY));
        sink.LineTo(new Vector2<double>(right, bottom - radiusY));
        sink.ArcTo(radiusX, radiusY, 0, false, true, new Vector2<double>(right - radiusX, bottom));
        sink.LineTo(new Vector2<double>(x + radiusX, bottom));
        sink.ArcTo(radiusX, radiusY, 0, false, true, new Vector2<double>(x, bottom - radiusY));
        sink.LineTo(new Vector2<double>(x, y + radiusY));
        sink.ArcTo(radiusX, radiusY, 0, false, true, new Vector2<double>(x + radiusX, y));
        sink.Close();
    }

    private static void Ellipse(SvgOutlineSink sink, double cx, double cy, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0)
        {
            return;
        }

        sink.MoveTo(new Vector2<double>(cx + rx, cy));
        sink.ArcTo(rx, ry, 0, true, true, new Vector2<double>(cx - rx, cy));
        sink.ArcTo(rx, ry, 0, true, true, new Vector2<double>(cx + rx, cy));
        sink.Close();
    }

    private static void Polygon(SvgOutlineSink sink, List<double> numbers)
    {
        for (var i = 0; i + 1 < numbers.Count; i += 2)
        {
            var point = new Vector2<double>(numbers[i], numbers[i + 1]);
            if (i == 0)
            {
                sink.MoveTo(point);
            }
            else
            {
                sink.LineTo(point);
            }
        }

        sink.Close();
    }

    private static double Number(XElement element, string name) => Optional(element, name) ?? 0;

    private static double? Optional(XElement element, string name)
    {
        var text = ((string)element.Attribute(name))?.Trim();
        if (string.IsNullOrEmpty(text) || text == "auto")
        {
            return null;
        }

        if (text.EndsWith("px", StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - 2);
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
