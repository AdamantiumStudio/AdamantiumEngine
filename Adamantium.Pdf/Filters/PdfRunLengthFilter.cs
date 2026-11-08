using System;

namespace Adamantium.Pdf.Filters;

/// <summary><c>RunLengthDecode</c>: a length byte n, then n + 1 bytes to copy (n below 128) or one byte to repeat
/// 257 - n times (n above 128); 128 ends the data.</summary>
internal static class PdfRunLengthFilter
{
    public static Memory<byte> Decode(ReadOnlySpan<byte> input, PdfDecodeLimits limits)
    {
        var output = new PdfOutputBuffer(input.Length * 2, limits.MaxDecodedLength);
        var position = 0;
        while (position < input.Length)
        {
            var length = input[position++];
            if (length == 128)
            {
                break;
            }

            if (length < 128)
            {
                var count = Math.Min(length + 1, input.Length - position);
                output.Add(input.Slice(position, count));
                position += count;
            }
            else if (position < input.Length)
            {
                output.Repeat(input[position++], 257 - length);
            }
        }

        return output.ToMemory();
    }
}
