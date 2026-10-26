using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Adamantium.Imaging.Png;

/// <summary>PNG for <see cref="BitmapLoader"/>.</summary>
public static class PngHelper
{
    /// <summary>Reads the PNG of <paramref name="size"/> bytes at <paramref name="pSource"/>.</summary>
    public static IRawBitmap LoadFromMemory(IntPtr pSource, ulong size)
    {
        if (size > (ulong)Array.MaxLength)
        {
            throw new NotSupportedException($"A PNG file of {size} bytes is more than one array holds.");
        }

        var data = new byte[size];
        Marshal.Copy(pSource, data, 0, (int)size);
        return PngImage.Read(data);
    }

    /// <summary>Writes <paramref name="img"/> as PNG.</summary>
    public static void SaveToStream(IRawBitmap img, Stream imageStream) => PngEncoder.Encode(img, imageStream);
}
