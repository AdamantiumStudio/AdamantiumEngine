using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Bmp;

/// <summary>Writes pictures as BMP: 24-bit BGR rows, bottom up, when every pixel is opaque; otherwise 32-bit pixels
/// with a BITMAPV4HEADER whose masks say where alpha is, as readers that keep alpha read it.</summary>
public static class BmpEncoder
{
    /// <summary>Writes <paramref name="image"/>'s first frame to <paramref name="output"/>: gray, RGB and RGBA pixels
    /// of 8 or 16 bits, in either channel order; others throw <see cref="NotSupportedException"/>.</summary>
    public static void Encode(IRawBitmap image, Stream output)
    {
        int width = (int)image.Width, height = (int)image.Height;
        var rgba = image.GetRgba8Pixels()
                   ?? throw new NotSupportedException($"BMP is not written from {(Format)image.PixelFormat} pixels.");
        var opaque = true;
        for (var i = 3; i < rgba.Length && opaque; i += 4)
        {
            opaque = rgba[i] == 255;
        }

        var bytes = opaque ? 3 : 4;
        var stride = (width * bytes + 3) & ~3;
        var headerSize = opaque ? 40 : 108;
        var pixelsAt = 14 + headerSize;
        var header = new byte[pixelsAt];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(2), (uint)(pixelsAt + (long)stride * height));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(10), (uint)pixelsAt);
        var info = header.AsSpan(14);
        BinaryPrimitives.WriteUInt32LittleEndian(info, (uint)headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(info[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(info[8..], height);
        BinaryPrimitives.WriteUInt16LittleEndian(info[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(info[14..], (ushort)(bytes * 8));
        BinaryPrimitives.WriteUInt32LittleEndian(info[20..], (uint)(stride * height));
        BinaryPrimitives.WriteInt32LittleEndian(info[24..], 2835);
        BinaryPrimitives.WriteInt32LittleEndian(info[28..], 2835);
        if (!opaque)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(info[16..], 3);
            BinaryPrimitives.WriteUInt32LittleEndian(info[40..], 0x00FF0000);
            BinaryPrimitives.WriteUInt32LittleEndian(info[44..], 0x0000FF00);
            BinaryPrimitives.WriteUInt32LittleEndian(info[48..], 0x000000FF);
            BinaryPrimitives.WriteUInt32LittleEndian(info[52..], 0xFF000000);
            BinaryPrimitives.WriteUInt32LittleEndian(info[56..], 0x73524742);
        }

        output.Write(header);
        var line = new byte[stride];
        for (var y = height - 1; y >= 0; y--)
        {
            for (var x = 0; x < width; x++)
            {
                var from = (y * width + x) * 4;
                var to = x * bytes;
                (line[to], line[to + 1], line[to + 2]) = (rgba[from + 2], rgba[from + 1], rgba[from]);
                if (!opaque)
                {
                    line[to + 3] = rgba[from + 3];
                }
            }

            output.Write(line);
        }

        output.Flush();
    }
}
