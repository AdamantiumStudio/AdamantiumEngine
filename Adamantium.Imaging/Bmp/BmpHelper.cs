using System;
using System.IO;

namespace Adamantium.Imaging.Bmp;

/// <summary>BMP for <see cref="BitmapLoader"/>.</summary>
public static class BmpHelper
{
    /// <summary>Reads the BMP of <paramref name="size"/> bytes at <paramref name="pSource"/>.</summary>
    public static unsafe IRawBitmap LoadFromMemory(IntPtr pSource, ulong size)
    {
        if (size > int.MaxValue)
        {
            throw new NotSupportedException($"A BMP file of {size} bytes is more than one span holds.");
        }

        return BmpImage.Read(new ReadOnlySpan<byte>((void*)pSource, (int)size));
    }

    /// <summary>Writes <paramref name="img"/> as BMP.</summary>
    public static void SaveToStream(IRawBitmap img, Stream imageStream) => BmpEncoder.Encode(img, imageStream);
}
