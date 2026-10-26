using System.IO;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging;

public static class RawBitmapExtension
{
    public static void Save(this IRawBitmap bitmap, string path, ImageFileType fileType)
    {
        BitmapLoader.Save(bitmap, path, fileType);
    }

    public static void Save(this IRawBitmap bitmap, Stream stream, ImageFileType fileType)
    {
        BitmapLoader.Save(bitmap, stream, fileType);
    }

    /// <summary>Frame <paramref name="frameIndex"/>'s pixels as RGBA of 8 bits a channel - for what takes nothing else:
    /// RGBA as it is, BGRA swapped, 16 bits by the high byte of each channel, RGB and BGR given opaque alpha, gray
    /// spread over the three; null for other formats.</summary>
    public static byte[] GetRgba8Pixels(this IRawBitmap bitmap, uint frameIndex = 0)
    {
        var pixels = bitmap.GetRawPixels(frameIndex);
        var count = (long)bitmap.Width * bitmap.Height;
        // Channels, bytes a channel, and where red is among them.
        var (channels, step, red) = (Format)bitmap.PixelFormat switch
        {
            Format.R8G8B8A8_UNORM or Format.R8G8B8A8_SRGB => (4, 1, 0),
            Format.B8G8R8A8_UNORM or Format.B8G8R8A8_SRGB => (4, 1, 2),
            Format.R8G8B8_UNORM or Format.R8G8B8_SRGB => (3, 1, 0),
            Format.B8G8R8_UNORM or Format.B8G8R8_SRGB => (3, 1, 2),
            Format.R8_UNORM or Format.R8_SRGB => (1, 1, 0),
            Format.R16G16B16A16_UNORM => (4, 2, 0),
            Format.R16G16B16_UNORM => (3, 2, 0),
            Format.R16_UNORM => (1, 2, 0),
            _ => (0, 0, 0)
        };
        var size = channels * step;
        if (size == 0 || pixels == null || pixels.LongLength < count * size)
        {
            return null;
        }

        if (size == 4 && red == 0)
        {
            return pixels;
        }

        // The high byte of a little-endian 16-bit channel is its second.
        var high = step - 1;
        var rgba = new byte[count * 4];
        for (long i = 0; i < count; i++)
        {
            var from = i * size + high;
            if (channels == 1)
            {
                rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = pixels[from];
                rgba[i * 4 + 3] = 255;
                continue;
            }

            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]) = (pixels[from + red * step], pixels[from + step],
                pixels[from + (2 - red) * step]);
            rgba[i * 4 + 3] = channels == 4 ? pixels[from + 3 * step] : (byte)255;
        }

        return rgba;
    }
}
