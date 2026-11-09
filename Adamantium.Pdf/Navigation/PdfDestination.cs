using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Navigation;

/// <summary>A place in a document to go to (ISO 32000-2, 12.3.2): a page, and how it is shown - coordinates in its
/// user space, null where the destination leaves the view as it is.</summary>
public sealed class PdfDestination
{
    internal PdfDestination(int pageIndex, PdfDestinationKind kind)
    {
        PageIndex = pageIndex;
        Kind = kind;
    }

    /// <summary>The page, from 0; -1 when it is not one of the document's.</summary>
    public int PageIndex { get; }

    public PdfDestinationKind Kind { get; }

    public double? Left { get; private init; }

    public double? Top { get; private init; }

    public double? Right { get; private init; }

    public double? Bottom { get; private init; }

    /// <summary>The zoom of an <see cref="PdfDestinationKind.Xyz"/> destination, 1 for 100 %; null to keep it.</summary>
    public double? Zoom { get; private init; }

    /// <summary>The explicit destination <paramref name="array"/>: a page and its view; null when it is none. The page
    /// of a destination in this document is one of its pages - a number out of their range is -1; that of another
    /// file's (<paramref name="remote"/>) is the number it gives.</summary>
    internal static PdfDestination From(PdfArray array, PdfDocument document, bool remote = false)
    {
        if (array is not { Count: > 0 })
        {
            return null;
        }

        var page = array[0] switch
        {
            PdfReference reference when !remote => document.PageIndexOf(reference.Id),
            PdfInteger number when remote => (int)System.Math.Clamp(number.Value, -1, int.MaxValue),
            PdfInteger number when number.Value >= 0 && number.Value < document.PageCount => (int)number.Value,
            _ => -1,
        };
        double? At(int index) => array.Count > index && document.Resolve(array[index]) is PdfInteger or PdfReal
            ? PdfValues.Number(array[index], document)
            : null;

        var kind = (document.Resolve(array.Count > 1 ? array[1] : null) as PdfName)?.Value ?? "Fit";
        return kind switch
        {
            "XYZ" => new PdfDestination(page, PdfDestinationKind.Xyz)
            {
                Left = At(2), Top = At(3), Zoom = At(4) is > 0 and var zoom ? zoom : null,
            },
            "FitH" => new PdfDestination(page, PdfDestinationKind.FitWidth) { Top = At(2) },
            "FitV" => new PdfDestination(page, PdfDestinationKind.FitHeight) { Left = At(2) },
            "FitR" => new PdfDestination(page, PdfDestinationKind.FitRectangle)
            {
                Left = At(2), Bottom = At(3), Right = At(4), Top = At(5),
            },
            "FitB" => new PdfDestination(page, PdfDestinationKind.FitContent),
            "FitBH" => new PdfDestination(page, PdfDestinationKind.FitContentWidth) { Top = At(2) },
            "FitBV" => new PdfDestination(page, PdfDestinationKind.FitContentHeight) { Left = At(2) },
            _ => new PdfDestination(page, PdfDestinationKind.Fit),
        };
    }
}
