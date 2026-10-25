using System;
using System.IO;
using System.IO.Compression;

namespace Adamantium.Imaging.Tiff;

/// <summary>Deflate in a zlib wrapper (RFC 1950/1951), as TIFF stores it - through .NET's own <see cref="ZLibStream"/>.
/// </summary>
internal static class TiffDeflate
{
    /// <summary>Inflates <paramref name="input"/> into <paramref name="output"/>; returns the bytes written. A stream
    /// cut short gives what it holds.</summary>
    public static unsafe int Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        fixed (byte* pointer = input)
        {
            using var compressed = new UnmanagedMemoryStream(pointer, input.Length);
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            var written = 0;
            try
            {
                while (written < output.Length)
                {
                    var read = inflater.Read(output[written..]);
                    if (read == 0)
                    {
                        break;
                    }

                    written += read;
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException)
            {
                if (written == 0)
                {
                    throw new InvalidDataException("A Deflate strip or tile is broken: " + exception.Message, exception);
                }
            }

            return written;
        }
    }

    /// <summary>Deflates <paramref name="input"/> - one strip or tile.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> input)
    {
        using var memory = new MemoryStream(input.Length / 2 + 64);
        using (var deflater = new ZLibStream(memory, CompressionLevel.Optimal))
        {
            deflater.Write(input);
        }

        return memory.ToArray();
    }
}
