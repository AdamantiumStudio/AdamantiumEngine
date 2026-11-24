namespace Adamantium.Pdf.Writing;

/// <summary>What makes a form a transparency group (ISO 32000-2, 11.6.6): drawn as one onto a backdrop of its own
/// (<see cref="Isolated"/>) or onto what lies under it, its parts over one another or each only over the backdrop
/// (<see cref="Knockout"/>), in <see cref="ColorSpace"/>.</summary>
public sealed record PdfTransparencyGroup(bool Isolated = true, bool Knockout = false, string ColorSpace = "DeviceRGB")
{
    /// <summary>An isolated group in DeviceRGB - a layer: what it draws is mixed first, then faded or masked as one.
    /// </summary>
    public static PdfTransparencyGroup Layer { get; } = new();
}
