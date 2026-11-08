using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Adamantium.Pdf.Filters;

/// <summary><c>FlateDecode</c>: zlib (RFC 1950/1951) through .NET's own inflater. Data without a zlib header is read
/// as raw Deflate; a stream cut short or with a bad checksum gives what it holds, as readers do.</summary>
internal static class PdfFlateFilter
{
    public static Memory<byte> Decode(ReadOnlyMemory<byte> input, PdfDecodeLimits limits)
    {
        var span = input.Span;
        var zlib = span.Length >= 2 && (span[0] & 0x0F) == 8 && ((span[0] << 8) | span[1]) % 31 == 0;
        var output = new PdfOutputBuffer((int)Math.Min(input.Length * 4L, limits.MaxDecodedLength),
            limits.MaxDecodedLength);
        using var compressed = MemoryMarshal.TryGetArray(input, out var segment)
            ? new MemoryStream(segment.Array, segment.Offset, segment.Count, false)
            : new MemoryStream(input.ToArray(), false);
        using Stream inflater = zlib
            ? new ZLibStream(compressed, CompressionMode.Decompress)
            : new DeflateStream(compressed, CompressionMode.Decompress);
        try
        {
            Span<byte> probe = stackalloc byte[1];
            while (true)
            {
                var free = output.Free(64 * 1024);
                if (free.IsEmpty)
                {
                    if (inflater.Read(probe) > 0)
                    {
                        throw new PdfDecodeLimitException(output.Limit);
                    }

                    break;
                }

                var read = inflater.Read(free);
                if (read == 0)
                {
                    break;
                }

                output.Advance(read);
            }
        }
        catch (InvalidDataException)
        {
        }

        return output.ToMemory();
    }
}
