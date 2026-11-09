using System;
using System.IO;

namespace Adamantium.Imaging.Jpeg;

/// <summary>JPEG for <see cref="BitmapLoader"/>.</summary>
public static class JpegHelper
{
    /// <summary>Decodes the JPEG of <paramref name="size"/> bytes at <paramref name="pSource"/>.</summary>
    public static JpegImage LoadFromMemory(IntPtr pSource, ulong size) =>
        LoadFromMemory(pSource, size, BitmapLoadOptions.Default);

    /// <summary>Decodes the JPEG of <paramref name="size"/> bytes at <paramref name="pSource"/> as
    /// <paramref name="options"/> say.</summary>
    public static unsafe JpegImage LoadFromMemory(IntPtr pSource, ulong size, BitmapLoadOptions options)
    {
        using var stream = new UnmanagedMemoryStream((byte*)pSource, (long)size);
        return new JpegDecoder(stream).Decode(options);
    }

    /// <summary>Writes <paramref name="img"/> as JPEG at the best quality.</summary>
    public static void SaveToStream(IRawBitmap img, Stream imageStream) => JpegEncoder.Encode(img, imageStream, 100);
}
