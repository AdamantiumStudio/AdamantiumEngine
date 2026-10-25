using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Adamantium.Imaging.Tiff;

/// <summary>TIFF for <see cref="BitmapLoader"/>.</summary>
public static class TiffHelper
{
    /// <summary>Reads the TIFF file of <paramref name="size"/> bytes at <paramref name="source"/>. The bytes are
    /// copied: pages after the first are decoded when asked for, after the caller's buffer is gone.</summary>
    public static IRawBitmap LoadFromMemory(IntPtr source, ulong size)
    {
        if (size > (ulong)Array.MaxLength)
        {
            throw new NotSupportedException($"The TIFF file of {size} bytes is too large to read whole.");
        }

        var bytes = new byte[size];
        Marshal.Copy(source, bytes, 0, bytes.Length);
        return TiffImage.Load(bytes);
    }

    /// <summary>Writes <paramref name="bitmap"/> to <paramref name="stream"/> as TIFF.</summary>
    public static void SaveToStream(IRawBitmap bitmap, Stream stream) => TiffEncoder.Save(bitmap, stream);
}
