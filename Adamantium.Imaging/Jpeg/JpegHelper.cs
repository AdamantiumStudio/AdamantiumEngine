using System;
using System.IO;

namespace Adamantium.Imaging.Jpeg;

/// <summary>JPEG for <see cref="BitmapLoader"/>.</summary>
public static class JpegHelper
{
    /// <summary>Decodes the JPEG of <paramref name="size"/> bytes at <paramref name="pSource"/>.</summary>
    public static unsafe JpegImage LoadFromMemory(IntPtr pSource, ulong size)
    {
        using var stream = new UnmanagedMemoryStream((byte*)pSource, (long)size);
        return new JpegDecoder(stream).Decode();
    }

    /// <summary>Writes <paramref name="img"/> as JPEG at the best quality.</summary>
    public static void SaveToStream(IRawBitmap img, Stream imageStream) => JpegEncoder.Encode(img, imageStream, 100);
}
