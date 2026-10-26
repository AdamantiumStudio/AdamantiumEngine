using System;
using System.IO;

namespace Adamantium.Imaging.Gif;

/// <summary>GIF for <see cref="BitmapLoader"/>.</summary>
public static class GifHelper
{
    /// <summary>Reads the GIF of <paramref name="size"/> bytes at <paramref name="pSource"/>; nothing of it is kept but
    /// its images' data.</summary>
    public static unsafe IRawBitmap LoadFromMemory(IntPtr pSource, ulong size)
    {
        if (size > int.MaxValue)
        {
            throw new NotSupportedException($"A GIF file of {size} bytes is more than one span holds.");
        }

        return GifImage.Read(new ReadOnlySpan<byte>((void*)pSource, (int)size));
    }

    /// <summary>Writes <paramref name="img"/> as GIF.</summary>
    public static void SaveToStream(IRawBitmap img, Stream imageStream) => GifEncoder.Encode(img, imageStream);
}
