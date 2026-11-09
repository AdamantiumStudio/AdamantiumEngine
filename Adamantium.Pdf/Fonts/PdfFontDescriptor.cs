using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Fonts;

/// <summary>A font descriptor (ISO 32000-2, 9.8): what a font is like, and its embedded program if it has one.
/// </summary>
public sealed class PdfFontDescriptor
{
    internal PdfFontDescriptor(PdfDictionary dictionary, PdfDocument document)
    {
        FontName = PdfValues.Name(dictionary[PdfFontKeys.FontName], document);
        FontFamily = (document.Resolve(dictionary[PdfFontKeys.FontFamily]) as PdfString)?.ToText();
        Flags = (PdfFontFlags)PdfValues.Integer(dictionary[PdfFontKeys.Flags], document);
        ItalicAngle = PdfValues.Number(dictionary[PdfFontKeys.ItalicAngle], document);
        FontWeight = PdfValues.Integer(dictionary[PdfFontKeys.FontWeight], document);
        MissingWidth = PdfValues.Number(dictionary[PdfFontKeys.MissingWidth], document);
        Ascent = PdfValues.Number(dictionary[PdfFontKeys.Ascent], document);
        Descent = PdfValues.Number(dictionary[PdfFontKeys.Descent], document);
        FontBBox = PdfRectangle.From(dictionary[PdfFontKeys.FontBBox], document);
        FontFile = document.Resolve(dictionary[PdfFontKeys.FontFile]) as PdfStream;
        FontFile2 = document.Resolve(dictionary[PdfFontKeys.FontFile2]) as PdfStream;
        FontFile3 = document.Resolve(dictionary[PdfFontKeys.FontFile3]) as PdfStream;
    }

    public string FontName { get; }

    public string FontFamily { get; }

    public PdfFontFlags Flags { get; }

    public double ItalicAngle { get; }

    /// <summary>100 to 900 as CSS weighs it; 0 when the descriptor does not say.</summary>
    public int FontWeight { get; }

    /// <summary>The width of a code the font's widths do not list, in thousandths of the size.</summary>
    public double MissingWidth { get; }

    /// <summary>How far glyphs rise above the baseline, in thousandths of the size; 0 when the descriptor does not say.
    /// </summary>
    public double Ascent { get; }

    /// <summary>How far glyphs fall below the baseline, in thousandths of the size, as a negative number; 0 when the
    /// descriptor does not say.</summary>
    public double Descent { get; }

    /// <summary>The box every glyph fits in, in glyph space; null when the descriptor has none.</summary>
    public PdfRectangle? FontBBox { get; }

    /// <summary>A Type 1 program.</summary>
    public PdfStream FontFile { get; }

    /// <summary>A TrueType program.</summary>
    public PdfStream FontFile2 { get; }

    /// <summary>A program its <c>/Subtype</c> names: bare CFF, CID-keyed CFF, or OpenType.</summary>
    public PdfStream FontFile3 { get; }

    public bool IsSymbolic => (Flags & PdfFontFlags.Symbolic) != 0;

    public bool IsEmbedding => FontFile != null || FontFile2 != null || FontFile3 != null;
}
