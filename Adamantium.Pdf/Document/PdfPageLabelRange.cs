namespace Adamantium.Pdf.Document;

/// <summary>Pages from <see cref="StartIndex"/> on, labeled in one style until the next range.</summary>
public readonly record struct PdfPageLabelRange(int StartIndex, PdfPageLabelStyle Style, string Prefix, int FirstNumber);
