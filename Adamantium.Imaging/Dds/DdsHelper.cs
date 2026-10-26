using System;
using System.IO;

namespace Adamantium.Imaging.Dds;

/// <summary>DDS for <see cref="BitmapLoader"/>.</summary>
public static class DdsHelper
{
    /// <summary>Reads the DDS of <paramref name="size"/> bytes at <paramref name="pSource"/>.</summary>
    public static unsafe IRawBitmap LoadFromMemory(IntPtr pSource, ulong size)
    {
        if (size > int.MaxValue)
        {
            throw new NotSupportedException($"A DDS file of {size} bytes is more than one span holds.");
        }

        return DdsImage.Read(new ReadOnlySpan<byte>((void*)pSource, (int)size));
    }

    /// <summary>Writes <paramref name="img"/> as DDS.</summary>
    public static void SaveToStream(IRawBitmap img, Stream imageStream) => DdsEncoder.Encode(img, imageStream);
}
