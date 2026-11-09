using System;
using System.Runtime.InteropServices;

namespace Adamantium.Imaging.Jpeg2000;

/// <summary>Reads JPEG 2000 files for <see cref="BitmapLoader"/>.</summary>
public static class Jpeg2000Helper
{
    public static Jpeg2000Bitmap LoadFromMemory(IntPtr source, ulong size)
    {
        var data = new byte[size];
        Marshal.Copy(source, data, 0, data.Length);
        return new Jpeg2000Bitmap(data, BitmapLoader.MaxPixels);
    }

    /// <summary>Whether <paramref name="header"/> starts a JP2 file or a bare codestream.</summary>
    public static bool IsJpeg2000(ReadOnlySpan<byte> header) => header switch
    {
        [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, ..] => true,
        [0xFF, 0x4F, 0xFF, 0x51, ..] => true,
        _ => false,
    };
}
