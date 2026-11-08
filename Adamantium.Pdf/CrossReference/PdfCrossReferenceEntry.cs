namespace Adamantium.Pdf.CrossReference;

/// <summary>One object's entry: its offset in the file, or the object stream that holds it and its index there.
/// </summary>
public readonly record struct PdfCrossReferenceEntry(
    PdfCrossReferenceEntryKind Kind,
    long Offset,
    int Generation,
    int StreamNumber = 0,
    int Index = 0)
{
    public static PdfCrossReferenceEntry Free { get; } = new(PdfCrossReferenceEntryKind.Free, 0, 0);
}
