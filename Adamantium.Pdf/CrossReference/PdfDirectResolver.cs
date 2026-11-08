using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.CrossReference;

/// <summary>Resolves nothing: what a cross-reference stream is read with, before any object can be found.</summary>
internal sealed class PdfDirectResolver : IPdfObjectResolver
{
    public static PdfDirectResolver Instance { get; } = new();

    public PdfObject Resolve(PdfObject value) => value is PdfReference ? PdfNull.Value : value;
}
