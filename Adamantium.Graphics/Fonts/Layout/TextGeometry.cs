using System;
using System.Collections.Generic;
using System.Globalization;
using Adamantium.Fonts;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Fonts.Layout;

internal static class TextGeometry
{
    public const char SoftHyphen = '­';

    public const char LineSeparator = (char)0x2028;

    public const char ParagraphSeparator = (char)0x2029;

    public const char ObjectReplacement = (char)0xFFFC;

    public static bool IsBlank(char symbol)
    {
        return symbol == ' ' || symbol == '\t';
    }

    public static bool IsTrailingSurrogate(string text, int index)
    {
        return index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]);
    }

    public static int CodepointAt(string text, int index) =>
        char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? char.ConvertToUtf32(text[index], text[index + 1])
            : text[index];

    public static bool IsHanOrKana(int codepoint) => codepoint is >= 0x3040 and <= 0x30FF or >= 0x31F0 and <= 0x31FF
        or >= 0x3005 and <= 0x3007 or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF
        or >= 0x20000 and <= 0x3FFFF;

    public static bool IsSpaceOrControl(int codepoint)
    {
        return CharUnicodeInfo.GetUnicodeCategory(codepoint) is UnicodeCategory.SpaceSeparator
            or UnicodeCategory.Control or UnicodeCategory.Format;
    }

    public static double SpaceWidth(IFont font, double size) =>
        font.GetAdvanceWidth(font.GetGlyphByCharacter(' ').Index) * size / font.UnitsPerEm;

    public static double LineAdvance(IFont font, double fontSize)
    {
        return (font.LineAscent + font.LineDescent + font.LineGap) * (fontSize / font.UnitsPerEm);
    }

    public static double BaselineInLine(IFont font, double scale)
    {
        return (font.LineGap / 2.0 + font.LineAscent) * scale;
    }

    public static Glyph HyphenGlyph(IFont font)
    {
        return font.TryGetGlyphIndex(0x2010, out var hyphen)
            ? font.GetGlyphByIndex(hyphen)
            : font.GetGlyphByCharacter('-');
    }

    public static IFont RunFont(IFont font, TextAttributes attributes, double fontSize) =>
        (attributes?.Font ?? font).AtOpticalSize((float)(attributes?.FontSize ?? fontSize));

    public static IFont DrawnFont(IFont font) =>
        font.Blend is not { } blend ? font : blend.Amount < 0.5f ? blend.From : blend.To;

    public static IEnumerable<(IFont Font, Glyph Glyph)> FieldsOf(IFont font, Glyph glyph)
    {
        if (font.Blend is not { } blend)
        {
            yield return (font, glyph);
            yield break;
        }

        yield return (blend.From, blend.From.GetGlyphByIndex(glyph.Index));
        yield return (blend.To, blend.To.GetGlyphByIndex(glyph.Index));
    }

    public static double Raise(IFont baseFont, WritingMode writingMode, TextAttributes attributes, IFont font,
        double size, double baseSize, double objectHeight = double.NaN)
    {
        var baseScale = baseSize / baseFont.UnitsPerEm;
        var scale = size / font.UnitsPerEm;
        var across = writingMode != WritingMode.VerticalRightToLeft;
        var ascender = double.IsNaN(objectHeight) ? font.Ascender * scale : objectHeight;
        var descender = double.IsNaN(objectHeight) ? font.Descender * scale : 0;
        return (attributes?.BaselineShift ?? 0) + attributes?.BaselineAlignment switch
        {
            BaselineAlignment.Superscript => baseFont.SuperscriptYOffset > 0
                ? baseFont.SuperscriptYOffset * baseScale
                : baseSize / 3,
            BaselineAlignment.Subscript => baseFont.SubscriptYOffset > 0
                ? -baseFont.SubscriptYOffset * baseScale
                : -baseSize / 7,
            BaselineAlignment.TextTop when across => baseFont.Ascender * baseScale - ascender,
            BaselineAlignment.TextBottom when across => baseFont.Descender * baseScale - descender,
            _ => 0,
        };
    }

    public static double LineRaise(WritingMode writingMode, TextAttributes attributes, char symbol, double lineBaseline,
        double lineHeight, IFont font, double size)
    {
        if (writingMode == WritingMode.VerticalRightToLeft)
        {
            return 0;
        }

        var objectSize = ObjectSizeOf(symbol, attributes);
        var ascent = objectSize?.Height ?? BaselineInLine(font, size / font.UnitsPerEm);
        var box = objectSize?.Height ?? LineAdvance(font, size);
        return attributes?.BaselineAlignment switch
        {
            BaselineAlignment.Top => lineBaseline - ascent,
            BaselineAlignment.Center => lineBaseline - (lineHeight - box) / 2 - ascent,
            BaselineAlignment.Bottom => lineBaseline - lineHeight + box - ascent,
            _ => 0,
        };
    }

    public static bool AlignsToLine(TextAttributes attributes) =>
        attributes?.BaselineAlignment is BaselineAlignment.Top or BaselineAlignment.Center or BaselineAlignment.Bottom;

    public static Size? ObjectSizeOf(char symbol, TextAttributes attributes) =>
        symbol == ObjectReplacement ? attributes?.ObjectSize : null;

    public static RectangleF ObjectRect(double pen, double baseline, Size size) =>
        new((float)pen, (float)(Math.Round(baseline) - size.Height), (float)size.Width, (float)size.Height);

    public static RectangleF CellRect(IFont font, double fontSize, double pen, double advance, double glyphBase)
    {
        var scale = fontSize / font.UnitsPerEm;
        return new RectangleF((float)pen, (float)(glyphBase - font.LineAscent * scale), (float)advance,
            (float)((font.LineAscent + font.LineDescent) * scale));
    }

    public static void Relay(GlyphWordData glyph, double baseline)
    {
        if (ObjectSizeOf(glyph.Symbol, glyph.Attributes) != null)
        {
            glyph.Rect.X = (float)glyph.PenX;
            return;
        }

        var origin = glyph.PenX + glyph.OffsetX;
        glyph.Rect = Widened(CalculateGlyphPosition(glyph.Font, glyph.Glyph, origin, baseline,
            glyph.FontSize / glyph.Font.UnitsPerEm), origin, glyph.HorizontalScale);
    }

    public static RectangleF Widened(RectangleF rect, double origin, double scale)
    {
        if (scale == 1)
        {
            return rect;
        }

        rect.X = (float)(origin + (rect.X - origin) * scale);
        rect.Width = (float)(rect.Width * scale);
        return rect;
    }

    public static Vector4F GlyphColor(GlyphWordData word)
    {
        return word.Attributes?.Foreground is { } color ? color.ToVector4() : FontItem.InheritedColor;
    }

    public static bool IsEmboldened(TextAttributes attributes) =>
        attributes?.Synthesis is { } synthesis && (synthesis & FontSynthesis.Weight) != 0;

    public static bool IsSlanted(TextAttributes attributes) =>
        attributes?.Synthesis is { } synthesis && (synthesis & FontSynthesis.Style) != 0;

    public static double Baseline(GlyphWordData word) =>
        DrawnRect(word).Bottom + word.Glyph.BoundingRectangle.Y * word.FontSize / word.Font.UnitsPerEm;

    public static RectangleF DrawnRect(GlyphWordData word) => word.Sideways
        ? new RectangleF(word.Rect.Right, word.Rect.Top, word.Rect.Height, word.Rect.Width)
        : word.Rect;

    public static double Pen(GlyphWordData word) => word.Upright || word.Sideways
        ? DrawnRect(word).X - word.Font.GetLeftSideBearing(word.Glyph.Index) * word.FontSize / word.Font.UnitsPerEm
            * word.HorizontalScale
        : word.PenX + word.OffsetX;

    public static RectangleF DrawnGlyphRect(GlyphWordData word, IFont font, Glyph glyph, double scale)
    {
        var baseline = Baseline(word);
        var rect = Widened(CalculateGlyphPosition(font, glyph, Pen(word), baseline, scale), Pen(word),
            word.HorizontalScale);
        if (word.Upright)
        {
            rect.Y += (float)(baseline - Math.Round(baseline));
        }

        return rect;
    }

    // A length that is no number counts as none.
    internal static double Finite(double? value) => value is { } number && double.IsFinite(number) ? number : 0;

    public static double InkRight(GlyphWordData word) =>
        IsSlanted(word.Attributes) && !word.Sideways && !word.Upright
            ? word.Rect.Right + Math.Max(0, Baseline(word) - word.Rect.Top) * FontSynthesisRules.Slant
            : word.Rect.Right;

    public static double InkLeft(GlyphWordData word) =>
        IsSlanted(word.Attributes) && !word.Sideways && !word.Upright
            ? word.Rect.Left - Math.Max(0, word.Rect.Bottom - Baseline(word)) * FontSynthesisRules.Slant
            : word.Rect.Left;

    public static RectangleF CalculateGlyphPosition(
        IFont font,
        Glyph glyph,
        double glyphLeft,
        double glyphBase,
        double scale)
    {
        var verticalShift = -glyph.BoundingRectangle.Y * scale;
        var horizontalShift = font.GetLeftSideBearing(glyph.Index) * scale;

        var glyphWidth = glyph.BoundingRectangle.Width * scale;
        var glyphHeight = glyph.BoundingRectangle.Height * scale;
        var glyphTop = (Math.Round(glyphBase) - glyphHeight) + verticalShift;
        glyphLeft += horizontalShift;

        // X stays sub-pixel: snapping fractional advances made equal gaps render a pixel apart.
        return new RectangleF((float)glyphLeft, (float)glyphTop, (float)glyphWidth, (float)glyphHeight);
    }
}
