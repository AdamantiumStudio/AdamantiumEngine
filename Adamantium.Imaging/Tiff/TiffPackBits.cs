using System;
using System.Collections.Generic;

namespace Adamantium.Imaging.Tiff;

/// <summary>PackBits run lengths (TIFF 6.0, section 9).</summary>
internal static class TiffPackBits
{
    /// <summary>Unpacks <paramref name="input"/> into <paramref name="output"/>; returns the bytes written. Stops when
    /// the output is full or the input ends.</summary>
    public static int Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var read = 0;
        var written = 0;
        while (read < input.Length && written < output.Length)
        {
            var header = (sbyte)input[read++];
            if (header >= 0)
            {
                var literal = Math.Min(Math.Min(header + 1, input.Length - read), output.Length - written);
                input.Slice(read, literal).CopyTo(output[written..]);
                read += header + 1;
                written += literal;
            }
            else if (header != -128 && read < input.Length)
            {
                var run = Math.Min(1 - header, output.Length - written);
                output.Slice(written, run).Fill(input[read++]);
                written += run;
            }
        }

        return written;
    }

    /// <summary>Packs <paramref name="input"/> - one row - onto the end of <paramref name="output"/>.</summary>
    public static void Encode(ReadOnlySpan<byte> input, List<byte> output)
    {
        var at = 0;
        while (at < input.Length)
        {
            var run = 1;
            while (at + run < input.Length && run < 128 && input[at + run] == input[at])
            {
                run++;
            }

            if (run > 1)
            {
                output.Add((byte)(sbyte)(1 - run));
                output.Add(input[at]);
                at += run;
                continue;
            }

            var start = at;
            while (at < input.Length && at - start < 128
                   && !(at + 1 < input.Length && input[at + 1] == input[at] && at + 2 < input.Length
                        && input[at + 2] == input[at]))
            {
                at++;
            }

            output.Add((byte)(at - start - 1));
            for (var i = start; i < at; i++)
            {
                output.Add(input[i]);
            }
        }
    }
}
