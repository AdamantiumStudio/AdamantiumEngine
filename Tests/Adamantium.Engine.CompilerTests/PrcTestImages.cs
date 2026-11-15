using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Pictures for PRC tests: a small PNG written by the PNG specification's chunk layout, and the size a PNG's
/// IHDR or a JPEG's start of frame gives - enough to tell an embedded picture is the image it says it is.</summary>
internal static class PrcTestImages
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A PNG of <paramref name="width"/> by <paramref name="height"/> RGB pixels, all of one colour.</summary>
    public static byte[] Png(int width, int height, byte red, byte green, byte blue)
    {
        var output = new MemoryStream();
        output.Write(PngSignature);
        var header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(output, "IHDR", header);
        var rows = new MemoryStream();
        using (var zlib = new ZLibStream(rows, CompressionLevel.Optimal, true))
        {
            for (var row = 0; row < height; row++)
            {
                zlib.WriteByte(0);
                for (var column = 0; column < width; column++)
                {
                    zlib.WriteByte(red);
                    zlib.WriteByte(green);
                    zlib.WriteByte(blue);
                }
            }
        }

        WriteChunk(output, "IDAT", rows.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>The size of a PNG or a JPEG; null when the bytes are not one.</summary>
    public static PrcTestImageSize SizeOf(byte[] data, string mimeType)
    {
        if (data == null)
        {
            return null;
        }

        return mimeType switch
        {
            "image/png" => PngSize(data),
            "image/jpeg" => JpegSize(data),
            _ => null
        };
    }

    private static PrcTestImageSize PngSize(byte[] data)
    {
        if (data.Length < 24 || !data.AsSpan(0, 8).SequenceEqual(PngSignature) || Encoding.ASCII.GetString(data, 12, 4) != "IHDR")
        {
            return null;
        }

        return new PrcTestImageSize(ReadBigEndian(data, 16), ReadBigEndian(data, 20));
    }

    private static PrcTestImageSize JpegSize(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return null;
        }

        var at = 2;
        while (at + 9 < data.Length)
        {
            if (data[at] != 0xFF)
            {
                return null;
            }

            var marker = data[at + 1];
            var length = (data[at + 2] << 8) | data[at + 3];
            var isFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isFrame)
            {
                return new PrcTestImageSize((data[at + 7] << 8) | data[at + 8], (data[at + 5] << 8) | data[at + 6]);
            }

            at += 2 + length;
        }

        return null;
    }

    private static void WriteChunk(Stream output, string type, byte[] content)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, (uint)content.Length);
        output.Write(length);
        var typed = new byte[4 + content.Length];
        Encoding.ASCII.GetBytes(type).CopyTo(typed, 0);
        content.CopyTo(typed, 4);
        output.Write(typed);
        var check = new byte[4];
        WriteBigEndian(check, 0, Crc(typed));
        output.Write(check);
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static void WriteBigEndian(byte[] into, int at, uint value)
    {
        into[at] = (byte)(value >> 24);
        into[at + 1] = (byte)(value >> 16);
        into[at + 2] = (byte)(value >> 8);
        into[at + 3] = (byte)value;
    }

    private static int ReadBigEndian(byte[] data, int at) => (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];
}

/// <summary>The size of a picture in pixels.</summary>
internal sealed class PrcTestImageSize
{
    public PrcTestImageSize(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }
}
