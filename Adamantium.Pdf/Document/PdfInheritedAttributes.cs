using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Document;

/// <summary>What a page takes from the page-tree nodes above it when it does not say itself (ISO 32000-2, 7.7.3.4).
/// </summary>
internal readonly record struct PdfInheritedAttributes(
    PdfObject Resources,
    PdfObject MediaBox,
    PdfObject CropBox,
    PdfObject Rotate)
{
    public PdfInheritedAttributes Below(PdfDictionary node) => new(
        node[PdfKeys.Resources] ?? Resources,
        node[PdfKeys.MediaBox] ?? MediaBox,
        node[PdfKeys.CropBox] ?? CropBox,
        node[PdfKeys.Rotate] ?? Rotate);
}
