using System;
using System.IO;

namespace Adamantium.Imaging.Tga;

/// <summary>TGA for <see cref="BitmapLoader"/>.</summary>
internal static class TgaHelper
{
    public static unsafe IRawBitmap LoadFromMemory(IntPtr source, ulong size)
    {
        if (size > int.MaxValue)
        {
            throw new NotSupportedException($"A TGA file of {size} bytes is more than one span holds.");
        }

        return TgaImage.Read(new ReadOnlySpan<byte>((void*)source, (int)size));
    }

    public static void SaveToStream(IRawBitmap bitmap, Stream imageStream) => TgaEncoder.Encode(bitmap, imageStream);
}
