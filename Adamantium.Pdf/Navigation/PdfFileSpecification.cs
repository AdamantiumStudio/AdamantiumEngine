using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Navigation;

/// <summary>File specifications (ISO 32000-2, 7.11): a file's name, as a string or a dictionary.</summary>
internal static class PdfFileSpecification
{
    /// <summary>The file name <paramref name="value"/> gives - the Unicode one (<c>UF</c>) before the portable one;
    /// null when it gives none.</summary>
    public static string NameOf(PdfObject value, PdfDocument document) => document.Resolve(value) switch
    {
        PdfString text => text.ToText(),
        PdfDictionary specification => (document.Resolve(specification["UF"]) as PdfString)?.ToText()
                                       ?? (document.Resolve(specification["F"]) as PdfString)?.ToText()
                                       ?? (document.Resolve(specification["Unix"]) as PdfString)?.ToText()
                                       ?? (document.Resolve(specification["DOS"]) as PdfString)?.ToText(),
        _ => null,
    };
}
