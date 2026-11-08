using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Parsing;

/// <summary>An array or dictionary being read, with the dictionary key waiting for its value.</summary>
internal sealed class PdfParserFrame
{
    public PdfParserFrame(PdfArray array, PdfDictionary dictionary)
    {
        Array = array;
        Dictionary = dictionary;
    }

    public PdfArray Array { get; }

    public PdfDictionary Dictionary { get; }

    public string Key { get; set; }

    public PdfObject Value => (PdfObject)Array ?? Dictionary;
}
