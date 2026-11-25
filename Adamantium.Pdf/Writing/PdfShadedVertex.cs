namespace Adamantium.Pdf.Writing;

/// <summary>A corner of a shaded triangle: where it is and its color, DeviceRGB 0..1.</summary>
public readonly record struct PdfShadedVertex(double X, double Y, double Red, double Green, double Blue);
