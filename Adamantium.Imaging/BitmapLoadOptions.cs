namespace Adamantium.Imaging;

/// <summary>How <see cref="BitmapLoader"/> reads a picture.</summary>
public sealed class BitmapLoadOptions
{
    /// <summary>Pictures read as they are stored.</summary>
    public static BitmapLoadOptions Default { get; } = new();

    /// <summary>Whether the ICC profile a PNG, JPEG, TIFF or JPEG 2000 file holds is applied: its pixels then come
    /// out in sRGB - a CMYK picture through the profile rather than the plain conversion - and
    /// <see cref="IRawBitmap.ColorProfile"/> is null. Off by default: a texture's values, a mask or a height map,
    /// stay as the file has them.</summary>
    public bool ApplyColorProfile { get; init; }
}
