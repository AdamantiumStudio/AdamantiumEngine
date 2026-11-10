namespace Adamantium.Pdf.Graphics;

/// <summary>What a page is drawn for: optional content may show on the screen and not on paper, or the other way round
/// (ISO 32000-2, 8.11.4.4).</summary>
public enum PdfOptionalContentUse
{
    View,
    Print,
}
