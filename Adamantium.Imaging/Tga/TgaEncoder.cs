using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Vulkan.Core;

namespace Adamantium.Imaging.Tga;

/// <summary>Writes pictures as TGA 2.0: gray when every pixel is gray and opaque, 24-bit color when every pixel is
/// opaque, else 32-bit with 8 alpha bits; top row first, run-length encoded row by row unless asked not to, with the
/// extension area saying what the alpha bits hold.</summary>
public static class TgaEncoder
{
    private const int ExtensionSize = 495;

    /// <summary>Writes <paramref name="image"/>'s first frame to <paramref name="output"/>: gray, RGB and RGBA pixels
    /// of 8 or 16 bits, in either channel order, up to 65535 a side; others throw
    /// <see cref="NotSupportedException"/>.</summary>
    public static void Encode(IRawBitmap image, Stream output, bool compress = true)
    {
        int width = (int)image.Width, height = (int)image.Height;
        if (width is < 1 or > ushort.MaxValue || height is < 1 or > ushort.MaxValue)
        {
            throw new NotSupportedException($"A TGA is 1 to 65535 pixels a side, not {width}×{height}.");
        }

        var rgba = image.GetRgba8Pixels()
                   ?? throw new NotSupportedException($"TGA is not written from {(Format)image.PixelFormat} pixels.");
        var count = width * height;
        bool gray = true, opaque = true;
        for (var i = 0; i < count && (gray || opaque); i++)
        {
            opaque &= rgba[i * 4 + 3] == 255;
            gray &= rgba[i * 4 + 3] == 255 && rgba[i * 4] == rgba[i * 4 + 1] && rgba[i * 4 + 1] == rgba[i * 4 + 2];
        }

        var pixelBytes = gray ? 1 : opaque ? 3 : 4;
        var header = new byte[18];
        header[2] = (byte)((gray ? 3 : 2) + (compress ? 8 : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)height);
        header[16] = (byte)(pixelBytes * 8);
        // Top row first; 8 alpha bits with the 32-bit pixels.
        header[17] = (byte)(0x20 | (pixelBytes == 4 ? 8 : 0));
        output.Write(header);

        var row = new byte[width * pixelBytes];
        var written = 18L;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var from = (y * width + x) * 4;
                var to = x * pixelBytes;
                if (gray)
                {
                    row[to] = rgba[from];
                    continue;
                }

                (row[to], row[to + 1], row[to + 2]) = (rgba[from + 2], rgba[from + 1], rgba[from]);
                if (pixelBytes == 4)
                {
                    row[to + 3] = rgba[from + 3];
                }
            }

            written += compress ? Packets(row, pixelBytes, output) : Plain(row, output);
        }

        // The extension area - its size and, last, the attributes type: 3 for alpha, 0 for none - and the footer.
        var extension = new byte[ExtensionSize];
        BinaryPrimitives.WriteUInt16LittleEndian(extension, ExtensionSize);
        extension[494] = (byte)(pixelBytes == 4 ? 3 : 0);
        output.Write(extension);
        var footer = new byte[26];
        BinaryPrimitives.WriteUInt32LittleEndian(footer, (uint)written);
        "TRUEVISION-XFILE.\0"u8.CopyTo(footer.AsSpan(8));
        output.Write(footer);
        output.Flush();
    }

    private static int Plain(byte[] row, Stream output)
    {
        output.Write(row);
        return row.Length;
    }

    // A row as run-length packets: two or more of one pixel as a run, the rest as raw packets, 128 pixels at most each.
    private static int Packets(byte[] row, int pixelBytes, Stream output)
    {
        var pixels = row.Length / pixelBytes;
        var written = 0;
        var at = 0;
        while (at < pixels)
        {
            var run = 1;
            while (at + run < pixels && run < 128 && Same(row, at, at + run, pixelBytes))
            {
                run++;
            }

            if (run >= 2)
            {
                output.WriteByte((byte)(0x80 | (run - 1)));
                output.Write(row, at * pixelBytes, pixelBytes);
                written += 1 + pixelBytes;
                at += run;
                continue;
            }

            // Raw pixels up to the next pair of equal ones.
            var raw = 1;
            while (at + raw < pixels && raw < 128 && !(at + raw + 1 < pixels && Same(row, at + raw, at + raw + 1, pixelBytes)))
            {
                raw++;
            }

            output.WriteByte((byte)(raw - 1));
            output.Write(row, at * pixelBytes, raw * pixelBytes);
            written += 1 + raw * pixelBytes;
            at += raw;
        }

        return written;
    }

    private static bool Same(byte[] row, int a, int b, int pixelBytes) =>
        row.AsSpan(a * pixelBytes, pixelBytes).SequenceEqual(row.AsSpan(b * pixelBytes, pixelBytes));
}
