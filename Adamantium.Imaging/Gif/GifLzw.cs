using System;
using System.IO;

namespace Adamantium.Imaging.Gif;

/// <summary>GIF's variable-length LZW (GIF89a, appendix F): codes of the minimum code size plus one bit up to 12, least
/// significant bit first, a clear code and an end code past the roots; the width grows when the next code to be made
/// no longer fits, and a full table is kept until the next clear code.</summary>
internal static class GifLzw
{
    /// <summary>Codes a table holds: 12 bits' worth.</summary>
    internal const int MaxCodes = 4096;

    /// <summary>Unpacks <paramref name="input"/> into <paramref name="output"/> and says how many bytes it filled -
    /// fewer when the data ends early or breaks off; what is past the output is not read.</summary>
    public static int Decode(ReadOnlySpan<byte> input, int minCodeSize, Span<byte> output) =>
        new GifLzwReader(input.ToArray(), minCodeSize).Read(output);

    /// <summary>Packs <paramref name="indices"/>, each below 2^<paramref name="minCodeSize"/>, as a clear code, the
    /// codes and the end code, in sub-blocks of up to 255 bytes and the empty block that ends them.</summary>
    public static void Encode(ReadOnlySpan<byte> indices, int minCodeSize, Stream output)
    {
        var clear = 1 << minCodeSize;
        var end = clear + 1;
        // The code of a string and one byte more, by the string's code and the byte; stamped with the table's
        // generation, so a clear code empties it without clearing it.
        var table = new int[MaxCodes << minCodeSize];
        var generation = 1;
        var width = minCodeSize + 1;
        var next = clear + 2;
        var writer = new GifLzwBlockWriter(output);
        writer.Write(clear, width);
        if (indices.IsEmpty)
        {
            writer.Write(end, width);
            writer.Finish();
            return;
        }

        int prefix = indices[0];
        for (var i = 1; i < indices.Length; i++)
        {
            var symbol = indices[i];
            var key = (prefix << minCodeSize) | symbol;
            var entry = table[key];
            if (entry >> 12 == generation)
            {
                prefix = entry & (MaxCodes - 1);
                continue;
            }

            writer.Write(prefix, width);
            if (next < MaxCodes)
            {
                table[key] = (generation << 12) | next++;
                // The reader makes each code one code later than the writer: it widens when the writer's next code
                // is one past the width.
                if (next > 1 << width && width < 12)
                {
                    width++;
                }
            }
            else
            {
                writer.Write(clear, width);
                (width, next) = (minCodeSize + 1, clear + 2);
                generation++;
            }

            prefix = symbol;
        }

        writer.Write(prefix, width);
        // The reader makes a code of the last one too, and widens for the end code if that fills the width.
        if (next >= 1 << width && width < 12)
        {
            width++;
        }

        writer.Write(end, width);
        writer.Finish();
    }
}
