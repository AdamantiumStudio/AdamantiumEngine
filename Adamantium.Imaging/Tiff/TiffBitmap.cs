namespace Adamantium.Imaging.Tiff;

/// <summary>One TIFF image ready to show: its pixels in <paramref name="Format"/>, rows top to bottom.</summary>
internal sealed record TiffBitmap(SurfaceFormat Format, int Width, int Height, int PixelBytes, byte[] Pixels);
