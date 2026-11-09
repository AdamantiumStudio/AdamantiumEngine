using Adamantium.Fonts;

namespace Adamantium.Pdf.Fonts;

/// <summary>Looks up glyphs in a font program's character map.</summary>
internal static class PdfFontGlyphs
{
    /// <summary>The glyph a font maps a text of one code point to; 0 for none, for a text of more code points - a
    /// ligature its map cannot hold - and for a broken surrogate.</summary>
    public static uint OfText(IFont font, string text)
    {
        if (font == null || string.IsNullOrEmpty(text))
        {
            return 0;
        }

        int codepoint;
        if (char.IsHighSurrogate(text[0]))
        {
            if (text.Length != 2 || !char.IsLowSurrogate(text[1]))
            {
                return 0;
            }

            codepoint = char.ConvertToUtf32(text[0], text[1]);
        }
        else if (text.Length != 1 || char.IsLowSurrogate(text[0]))
        {
            return 0;
        }
        else
        {
            codepoint = text[0];
        }

        return OfCode(font, (uint)codepoint);
    }

    /// <summary>The glyph a font maps a code to; 0 when it maps it to none.</summary>
    public static uint OfCode(IFont font, uint code) =>
        font != null && font.TryGetGlyphIndex((int)code, out var glyph) ? glyph : 0;
}
