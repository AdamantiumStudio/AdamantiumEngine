using System.Collections.Generic;

namespace Adamantium.Pdf.Navigation;

/// <summary>An item of a document's outline - its bookmarks (ISO 32000-2, 12.3.3): its title, where it goes, its
/// items below.</summary>
public sealed class PdfOutlineItem
{
    internal PdfOutlineItem(string title, PdfAction action, List<PdfOutlineItem> children)
    {
        Title = title ?? "";
        Action = action;
        Children = children;
    }

    public string Title { get; }

    /// <summary>What choosing it does - a <see cref="PdfActionKind.GoTo"/> for an item with a destination; null for an
    /// item that goes nowhere.</summary>
    public PdfAction Action { get; }

    public IReadOnlyList<PdfOutlineItem> Children { get; }

    /// <summary>Whether its items show (<c>Count</c> above 0).</summary>
    public bool IsOpen { get; internal init; }

    public bool IsBold { get; internal init; }

    public bool IsItalic { get; internal init; }

    /// <summary>Its color, DeviceRGB 0..1 (<c>C</c>); null for black.</summary>
    public IReadOnlyList<double> Color { get; internal init; }
}
