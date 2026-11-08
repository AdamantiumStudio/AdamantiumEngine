using System;
using Adamantium.Core.Compression;

namespace Adamantium.Pdf.Filters;

/// <summary><c>LZWDecode</c> through the engine's LZW. The decoded size is not stored, so the output starts at a guess
/// and is decoded again into twice the room while it comes out full.</summary>
internal static class PdfLzwFilter
{
    public static Memory<byte> Decode(ReadOnlySpan<byte> input, bool earlyChange, PdfDecodeLimits limits)
    {
        var largest = Math.Min(Array.MaxLength, limits.MaxDecodedLength + 1L);
        var size = (int)Math.Min(largest, Math.Max(4096L, input.Length * 4L));
        while (true)
        {
            var output = new byte[size];
            var written = Lzw.Decode(input, output, earlyChange);
            if (written < size)
            {
                return output.AsMemory(0, written);
            }

            if (size >= largest)
            {
                throw new PdfDecodeLimitException(limits.MaxDecodedLength);
            }

            size = (int)Math.Min(largest, size * 2L);
        }
    }
}
