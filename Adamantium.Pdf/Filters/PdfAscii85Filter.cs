using System;
using Adamantium.Pdf.Parsing;

namespace Adamantium.Pdf.Filters;

/// <summary><c>ASCII85Decode</c>: groups of five characters <c>!</c> to <c>u</c> into four bytes, <c>z</c> for four
/// zeros, up to <c>~&gt;</c>; a short last group of n characters gives n - 1 bytes.</summary>
internal static class PdfAscii85Filter
{
    public static Memory<byte> Decode(ReadOnlySpan<byte> input, PdfDecodeLimits limits)
    {
        if (input.StartsWith("<~"u8))
        {
            input = input[2..];
        }

        var output = new PdfOutputBuffer(input.Length, limits.MaxDecodedLength);
        Span<byte> word = stackalloc byte[4];
        uint value = 0;
        var count = 0;
        foreach (var current in input)
        {
            if (current == '~')
            {
                break;
            }

            if (PdfCharacters.IsWhiteSpace(current))
            {
                continue;
            }

            if (current == 'z' && count == 0)
            {
                output.Repeat(0, 4);
                continue;
            }

            if (current < '!' || current > 'u')
            {
                continue;
            }

            value = unchecked(value * 85 + (uint)(current - '!'));
            count++;
            if (count == 5)
            {
                Write(output, word, value, 4);
                value = 0;
                count = 0;
            }
        }

        if (count > 1)
        {
            for (var i = count; i < 5; i++)
            {
                value = unchecked(value * 85 + 84);
            }

            Write(output, word, value, count - 1);
        }

        return output.ToMemory();
    }

    private static void Write(PdfOutputBuffer output, Span<byte> word, uint value, int count)
    {
        word[0] = (byte)(value >> 24);
        word[1] = (byte)(value >> 16);
        word[2] = (byte)(value >> 8);
        word[3] = (byte)value;
        output.Add(word[..count]);
    }
}
