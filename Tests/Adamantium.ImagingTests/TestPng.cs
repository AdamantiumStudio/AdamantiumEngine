using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Adamantium.Imaging.Png;

namespace Adamantium.ImagingTests;

// Builds PNG files byte by byte, so the decoder is tested on streams it did not write itself.
internal static class TestPng
{
    public static byte[] Zlib(byte[] data, System.IO.Compression.CompressionLevel level)
    {
        var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, level, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    public static byte[] Build(int width, int height, byte bitDepth, byte colorType, byte[] idat,
        params (string Type, byte[] Data)[] beforeIdat)
    {
        var stream = new MemoryStream();
        stream.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = bitDepth;
        header[9] = colorType;

        Chunk(stream, "IHDR", header);
        foreach (var (type, data) in beforeIdat)
        {
            Chunk(stream, type, data);
        }

        Chunk(stream, "IDAT", idat);
        Chunk(stream, "IEND", []);
        return stream.ToArray();
    }

    public static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);

        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(typeAndData);

        BinaryPrimitives.WriteUInt32BigEndian(number, PngCrc.Compute(typeAndData));
        stream.Write(number);
    }
}
