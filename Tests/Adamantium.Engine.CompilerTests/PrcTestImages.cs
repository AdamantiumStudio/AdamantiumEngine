using System.IO;
using Adamantium.Imaging;
using Adamantium.Imaging.Png;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Pictures for PRC tests, made and read by the engine's own imaging.</summary>
internal static class PrcTestImages
{
    /// <summary>A PNG of <paramref name="width"/> by <paramref name="height"/> opaque pixels of one color.</summary>
    public static byte[] CreatePng(int width, int height, byte red, byte green, byte blue)
    {
        var pixels = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            pixels[pixel * 4] = red;
            pixels[pixel * 4 + 1] = green;
            pixels[pixel * 4 + 2] = blue;
            pixels[pixel * 4 + 3] = byte.MaxValue;
        }

        using var output = new MemoryStream();
        PngEncoder.Encode(new FrameBitmap((uint)width, (uint)height, SurfaceFormat.R8G8B8A8.UNorm, [pixels]), output);
        return output.ToArray();
    }

    /// <summary>The picture <paramref name="data"/> holds, decoded; it throws when the bytes are not a picture.</summary>
    public static IRawBitmap Decode(byte[] data) => BitmapLoader.Load(new MemoryStream(data));
}
