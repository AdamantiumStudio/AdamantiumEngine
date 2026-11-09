using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Fonts;

/// <summary>Reads fonts out of a file by their dictionaries' <c>/Subtype</c>.</summary>
public static class PdfFonts
{
    /// <summary>The font of a font dictionary; null when it is not one. An indirect font is read once per document,
    /// however many pages use it.</summary>
    public static PdfFont Create(PdfObject value, PdfDocument document)
    {
        if (value is PdfReference reference && document.CachedFont(reference.Id) is { } known)
        {
            return known;
        }

        if (document.Resolve(value) is not PdfDictionary dictionary)
        {
            return null;
        }

        PdfFont font = PdfValues.Name(dictionary[PdfFontKeys.Subtype], document) switch
        {
            PdfFontTypes.Type0 => new PdfType0Font(dictionary, document),
            PdfFontTypes.Type3 => new PdfType3Font(dictionary, document),
            PdfFontTypes.Type1 or PdfFontTypes.MMType1 or PdfFontTypes.TrueType => new PdfSimpleFont(dictionary,
                document),
            _ => null,
        };
        if (font != null && value is PdfReference read)
        {
            document.CacheFont(read.Id, font);
        }

        return font;
    }
}
