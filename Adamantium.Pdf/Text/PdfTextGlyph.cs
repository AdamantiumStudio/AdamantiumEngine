using Adamantium.Pdf.Document;

namespace Adamantium.Pdf.Text;

/// <summary>One glyph of a page's text: what it stands for, where it stands, and the font it is shown in.</summary>
public sealed class PdfTextGlyph
{
    internal PdfTextGlyph(string text, PdfQuad quad, PdfPoint origin, PdfPoint end, double size, string fontName,
        uint code, bool vertical)
    {
        Text = text ?? "";
        Quad = quad;
        Origin = origin;
        End = end;
        Size = size;
        FontName = fontName;
        Code = code;
        IsVertical = vertical;
    }

    /// <summary>The text the glyph stands for - none for a glyph the font says nothing of, several characters for a
    /// ligature, or a marked span's actual text.</summary>
    public string Text { get; }

    /// <summary>Its box: across its advance, from the font's descent to its ascent.</summary>
    public PdfQuad Quad { get; }

    /// <summary>Where it starts on its baseline.</summary>
    public PdfPoint Origin { get; }

    /// <summary>Where the next glyph would start without spacing.</summary>
    public PdfPoint End { get; }

    /// <summary>Its size on the page, in points.</summary>
    public double Size { get; }

    public string FontName { get; }

    /// <summary>The code the string gave it.</summary>
    public uint Code { get; }

    /// <summary>Whether it is set top to bottom.</summary>
    public bool IsVertical { get; }
}
