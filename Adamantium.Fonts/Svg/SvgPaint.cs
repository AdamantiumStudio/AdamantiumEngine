using System;
using System.Globalization;
using Adamantium.Fonts.Tables;
using Adamantium.Mathematics;

namespace Adamantium.Fonts.Svg;

internal readonly struct SvgPaint
{
    private SvgPaint(SvgPaintKind kind, Color color, string reference, string fallback)
    {
        Kind = kind;
        Color = color;
        Reference = reference;
        Fallback = fallback;
    }

    public SvgPaintKind Kind { get; }

    public Color Color { get; }

    public string Reference { get; }

    public string Fallback { get; }

    public static SvgPaint None => new(SvgPaintKind.None, default, null, null);

    public static SvgPaint Black => new(SvgPaintKind.Color, Colors.Black, null, null);

    public static SvgPaint Parse(string text, ColorPaletteTable palettes, int palette)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text == "none")
        {
            return None;
        }

        if (text == "currentColor")
        {
            return new SvgPaint(SvgPaintKind.CurrentColor, default, null, null);
        }

        if (text.StartsWith("url(", StringComparison.Ordinal))
        {
            var close = text.IndexOf(')');
            return close < 0
                ? None
                : new SvgPaint(SvgPaintKind.Reference, default, text.Substring(0, close + 1), text.Substring(close + 1).Trim());
        }

        return TryParseColor(text, palettes, palette, out var color)
            ? new SvgPaint(SvgPaintKind.Color, color, null, null)
            : None;
    }

    public static bool TryParseColor(string text, ColorPaletteTable palettes, int palette, out Color color)
    {
        color = default;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.StartsWith("var(", StringComparison.Ordinal)
            ? TryParseVariable(text, palettes, palette, out color)
            : Css.TryParseColor(text, out color);
    }

    private static bool TryParseVariable(string text, ColorPaletteTable palettes, int palette, out Color color)
    {
        color = default;
        var close = text.LastIndexOf(')');
        if (close < 4)
        {
            return false;
        }

        var inside = text.Substring(4, close - 4);
        var comma = inside.IndexOf(',');
        var name = (comma < 0 ? inside : inside.Substring(0, comma)).Trim();
        if (name.StartsWith("--color", StringComparison.Ordinal) &&
            int.TryParse(name.Substring(7), NumberStyles.None, CultureInfo.InvariantCulture, out var entry) &&
            palettes?.GetColor(palette, entry) is { } found)
        {
            color = found;
            return true;
        }

        return comma >= 0 && TryParseColor(inside.Substring(comma + 1), palettes, palette, out color);
    }
}
