using System;
using Adamantium.Pdf.Parsing;

namespace Adamantium.Pdf.Filters;

/// <summary><c>ASCIIHexDecode</c>: pairs of hexadecimal digits up to <c>&gt;</c>, white space and other bytes skipped,
/// a lone last digit followed by zero.</summary>
internal static class PdfAsciiHexFilter
{
    public static Memory<byte> Decode(ReadOnlySpan<byte> input)
    {
        var output = new byte[input.Length / 2 + 1];
        var count = 0;
        var high = -1;
        foreach (var value in input)
        {
            if (value == '>')
            {
                break;
            }

            var digit = PdfCharacters.HexValue(value);
            if (digit < 0)
            {
                continue;
            }

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                output[count++] = (byte)((high << 4) | digit);
                high = -1;
            }
        }

        if (high >= 0)
        {
            output[count++] = (byte)(high << 4);
        }

        return output.AsMemory(0, count);
    }
}
