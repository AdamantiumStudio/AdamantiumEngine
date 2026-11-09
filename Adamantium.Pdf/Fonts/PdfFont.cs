using System;
using Adamantium.Fonts;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Fonts;

/// <summary>A font a page shows text with (ISO 32000-2, 9.5): how its strings split into codes, the width and glyph
/// of each code, and the text it stands for. A font not embedded in the file is drawn with one of the system's - by
/// its name first, else one of the same metrics - and says so through <see cref="Substitute"/>. Like the document it
/// comes from, a font is read from one thread at a time.</summary>
public abstract class PdfFont
{
    private protected PdfFont(PdfDictionary dictionary, PdfDocument document)
    {
        Dictionary = dictionary;
        Subtype = PdfValues.Name(dictionary[PdfFontKeys.Subtype], document);
        BaseFont = PdfValues.Name(dictionary[PdfFontKeys.BaseFont], document);
        ToUnicode = dictionary[PdfFontKeys.ToUnicode] != null
            ? PdfCMapReader.Read(dictionary[PdfFontKeys.ToUnicode], document)
            : null;
    }

    public PdfDictionary Dictionary { get; }

    public string Subtype { get; }

    /// <summary>The font's PostScript name as the file gives it, with the six-letter tag of a subset.</summary>
    public string BaseFont { get; }

    /// <summary>The name without a subset's tag (<c>ABCDEF+</c>).</summary>
    public string Name => PdfFontNames.WithoutSubsetTag(BaseFont);

    public PdfFontDescriptor Descriptor { get; private protected set; }

    /// <summary>The font program glyphs are drawn from: the embedded one, or the system font that stands in for it;
    /// null when neither could be had, or for a Type 3 font, which draws with content streams.</summary>
    public Typeface Typeface { get; private protected set; }

    public bool IsEmbedded { get; private protected set; }

    /// <summary>The system font drawing a font the file does not embed, by its full name; null when the font is
    /// embedded or no system font was found.</summary>
    public string Substitute { get; private protected set; }

    /// <summary>The ToUnicode CMap, when the font has one.</summary>
    public PdfCMap ToUnicode { get; }

    /// <summary>Whether text is written top to bottom.</summary>
    public virtual bool IsVertical => false;

    /// <summary>The code at <paramref name="position"/> of a string; its length is 0 past the end.</summary>
    public abstract PdfCharacter ReadCharacter(ReadOnlySpan<byte> text, int position);

    /// <summary>How far the character advances, in text space at a font size of 1: horizontally, or downwards for
    /// vertical text.</summary>
    public abstract double WidthOf(PdfCharacter character);

    /// <summary>The glyph of <see cref="Typeface"/> that draws the character; 0, the missing glyph, when there is
    /// none.</summary>
    public abstract uint GlyphOf(PdfCharacter character);

    /// <summary>The text the character stands for: by the ToUnicode CMap, else by the font's encoding; null when
    /// neither says. A one-byte code is also looked up as two bytes, as some files write a simple font's
    /// ToUnicode.</summary>
    public string TextOf(PdfCharacter character)
    {
        var text = ToUnicode?.TextOf(character.Code, character.Length);
        if (text == null && character.Length == 1)
        {
            text = ToUnicode?.TextOf(character.Code, 2);
        }

        return text ?? EncodedText(character);
    }

    private protected abstract string EncodedText(PdfCharacter character);
}
