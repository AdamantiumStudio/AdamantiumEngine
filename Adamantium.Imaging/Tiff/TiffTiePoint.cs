namespace Adamantium.Imaging.Tiff;

/// <summary>A raster point (<paramref name="Column"/>, <paramref name="Row"/>) tied to the model point
/// (<paramref name="X"/>, <paramref name="Y"/>, <paramref name="Z"/>) - one of a GeoTIFF's ModelTiepoint.</summary>
public readonly record struct TiffTiePoint(double Column, double Row, double X, double Y, double Z);
