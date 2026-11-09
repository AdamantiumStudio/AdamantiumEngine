namespace Adamantium.Pdf.Document;

/// <summary>A point in a page's default user space, in points from its origin, y upward.</summary>
public readonly record struct PdfPoint(double X, double Y);
