using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Svg;

namespace Adamantium.Fonts;

/// <summary>CSS values as SVG glyphs and HTML styles write them: a style's declarations and colors.</summary>
public static class Css
{
    private static readonly Regex Important = new(@"!\s*important\s*$", RegexOptions.IgnoreCase);

    /// <summary>The declarations of a style - <c>"color: red; font-family: 'A;B' !important"</c> - in order: each
    /// name and value trimmed, <c>!important</c> taken off the value and told apart. A semicolon in quotes or brackets
    /// does not end a declaration; one without a name and a colon is left out.</summary>
    public static IReadOnlyList<CssDeclaration> Declarations(string block)
    {
        var declarations = new List<CssDeclaration>();
        foreach (var text in Split(block ?? string.Empty, ';'))
        {
            var colon = text.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var value = text.Substring(colon + 1).Trim();
            var important = Important.Match(value);
            if (important.Success)
            {
                value = value.Substring(0, important.Index).Trim();
            }

            declarations.Add(new CssDeclaration(text.Substring(0, colon).Trim(), value, important.Success));
        }

        return declarations;
    }

    /// <summary>A CSS color: <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c> or <c>#rrggbbaa</c>, <c>rgb()</c> or
    /// <c>rgba()</c> with numbers or percents, <c>transparent</c>, or a named color in any case ("grey" spellings
    /// included); false for anything else.</summary>
    public static bool TryParseColor(string text, out Color color)
    {
        color = default;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text[0] == '#')
        {
            return TryParseHex(text.Substring(1), out color);
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgb(text, out color);
        }

        if (text.Equals("zero", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Equals("rebeccapurple", StringComparison.OrdinalIgnoreCase))
        {
            color = Color.FromRgba(0x66, 0x33, 0x99, 255);
            return true;
        }

        var grey = text.IndexOf("grey", StringComparison.OrdinalIgnoreCase);
        return Colors.TryGetNamed(grey < 0 ? text : text.Substring(0, grey) + "gray" + text.Substring(grey + 4), out color);
    }

    internal static int Next(string css, int from, char first, char second)
    {
        var depth = 0;
        for (var i = from; i < css.Length; i++)
        {
            var c = css[i];
            if (c is '"' or '\'')
            {
                i = SkipString(css, i);
            }
            else if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && (c == first || c == second))
            {
                return i;
            }
        }

        return css.Length;
    }

    internal static int SkipString(string css, int start)
    {
        var i = start + 1;
        while (i < css.Length && css[i] != css[start])
        {
            i += css[i] == '\\' ? 2 : 1;
        }

        return Math.Min(i, css.Length - 1);
    }

    private static IEnumerable<string> Split(string text, char separator)
    {
        var from = 0;
        while (from <= text.Length)
        {
            var at = Next(text, from, separator, separator);
            yield return text.Substring(from, at - from);
            from = at + 1;
        }
    }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = default;
        foreach (var c in hex)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        switch (hex.Length)
        {
            case 3:
            case 4:
                var r = (byte)(Nibble(hex, 0) * 17);
                var g = (byte)(Nibble(hex, 1) * 17);
                var b = (byte)(Nibble(hex, 2) * 17);
                var a = hex.Length == 4 ? (byte)(Nibble(hex, 3) * 17) : (byte)255;
                color = Color.FromRgba(r, g, b, a);
                return true;
            case 6:
                color = Color.FromRgba((byte)(value >> 16), (byte)(value >> 8), (byte)value, 255);
                return true;
            case 8:
                color = Color.FromRgba((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
                return true;
            default:
                return false;
        }
    }

    private static int Nibble(string hex, int at) =>
        int.Parse(hex.Substring(at, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static bool TryParseRgb(string text, out Color color)
    {
        color = default;
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');
        if (open < 0 || close < open)
        {
            return false;
        }

        var parts = text.Substring(open + 1, close - open - 1).Split(',', ' ', '/');
        var channels = new double[4];
        var count = 0;
        foreach (var part in parts)
        {
            var value = part.Trim();
            if (value.Length == 0 || count == 4)
            {
                continue;
            }

            var percent = value.EndsWith("%", StringComparison.Ordinal);
            var numbers = SvgPathData.ReadNumbers(percent ? value.TrimEnd('%') : value);
            if (numbers.Count != 1)
            {
                return false;
            }

            channels[count] = count < 3
                ? (percent ? numbers[0] * 255 / 100 : numbers[0])
                : (percent ? numbers[0] / 100 : numbers[0]) * 255;
            count++;
        }

        if (count < 3)
        {
            return false;
        }

        color = Color.FromRgba(Channel(channels[0]), Channel(channels[1]), Channel(channels[2]),
            count == 4 ? Channel(channels[3]) : (byte)255);
        return true;
    }

    private static byte Channel(double value) =>
        (byte)Math.Max(0, Math.Min(255, Math.Round(value, MidpointRounding.AwayFromZero)));
}

/// <summary>A declaration of a CSS style: its property's name, its value and whether it is <c>!important</c>.</summary>
public sealed class CssDeclaration
{
    public CssDeclaration(string name, string value, bool important)
    {
        Name = name;
        Value = value;
        Important = important;
    }

    public string Name { get; }

    public string Value { get; }

    public bool Important { get; }
}
