using System;
using System.Globalization;
using System.Xml.Linq;
using Adamantium.Fonts.Tables;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Svg;

internal sealed class SvgStyle
{
    public static readonly SvgStyle Initial = new()
    {
        Fill = SvgPaint.Black,
        FillOpacity = 1,
        Opacity = 1,
    };

    public SvgPaint Fill { get; private set; }

    public double FillOpacity { get; private set; }

    public bool EvenOdd { get; private set; }

    public Color? CurrentColor { get; private set; }

    public double Opacity { get; private set; }

    public SvgStyle Inherit(XElement element, ColorPaletteTable palettes, int palette, bool withOpacity)
    {
        var fill = Property(element, "fill");
        var fillOpacity = Property(element, "fill-opacity");
        var fillRule = Property(element, "fill-rule");
        var color = Property(element, "color");
        var opacity = Property(element, "opacity");
        return new SvgStyle
        {
            Fill = fill != null && fill != "inherit" ? SvgPaint.Parse(fill, palettes, palette) : Fill,
            FillOpacity = Fraction(fillOpacity) ?? FillOpacity,
            EvenOdd = fillRule != null && fillRule != "inherit" ? fillRule == "evenodd" : EvenOdd,
            CurrentColor = color != null && SvgPaint.TryParseColor(color, palettes, palette, out var parsed)
                ? parsed
                : CurrentColor,
            Opacity = withOpacity ? Opacity * (Fraction(opacity) ?? 1) : Opacity,
        };
    }

    // A property as CSS cascades it: an !important declaration of the style attribute, then of the document's style
    // sheet, then the style attribute, the sheet, and last the presentation attribute.
    public static string Property(XElement element, string name)
    {
        var style = (string)element.Attribute("style");
        var inline = style != null ? SvgStyleSheet.Pick(SvgStyleSheet.Declarations(style), name) : null;

        if (inline is { Important: true })
        {
            return inline.Value;
        }

        var sheet = element.Document?.Annotation<SvgStyleSheet>()?.Property(element, name);
        if (sheet is { Important: true })
        {
            return sheet.Value;
        }

        var attribute = ((string)element.Attribute(name))?.Trim();
        return inline?.Value ?? sheet?.Value ?? (Valid(name, attribute) ? attribute : null);
    }

    // Whether a value is one the property takes: one it does not is dropped, as CSS drops it, and the property is set
    // by what comes after in the cascade, or inherited.
    public static bool Valid(string name, string value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (value == "inherit")
        {
            return true;
        }

        switch (name.ToLowerInvariant())
        {
            case "fill":
                return value is "none" or "currentColor" || IsUrl(value) || IsColor(value);
            case "color" or "stop-color":
                return value == "currentColor" || IsColor(value);
            case "opacity" or "fill-opacity" or "stop-opacity":
                return Fraction(value) != null;
            case "fill-rule" or "clip-rule":
                return value is "nonzero" or "evenodd";
            case "clip-path":
                return value == "none" || IsUrl(value);
            default:
                return true;
        }

        static bool IsUrl(string text) => text.StartsWith("url(", StringComparison.Ordinal) && text.IndexOf(')') > 0;

        static bool IsColor(string text) =>
            text.StartsWith("var(", StringComparison.Ordinal) || SvgPaint.TryParseColor(text, null, 0, out _);
    }

    public static double? Fraction(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var percent = text.EndsWith("%", StringComparison.Ordinal);
        if (!double.TryParse(percent ? text.Substring(0, text.Length - 1) : text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Math.Max(0, Math.Min(1, percent ? value / 100 : value));
    }
}
