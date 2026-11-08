using System;
using System.Collections.Generic;

namespace Adamantium.PdfTests;

/// <summary>LZW as PDF writes it, most significant bit first, with or without the early change - for the tests.
/// </summary>
internal static class LzwTestEncoder
{
    public static byte[] Encode(ReadOnlySpan<byte> input, bool earlyChange = true)
    {
        var early = earlyChange ? 0 : 1;
        var bytes = new List<byte>();
        ulong bits = 0;
        var count = 0;
        void Write(int code, int width)
        {
            bits = (bits << width) | (uint)code;
            count += width;
            while (count >= 8)
            {
                count -= 8;
                bytes.Add((byte)(bits >> count));
            }
        }

        var table = new Dictionary<int, int>();
        var width = 9;
        var next = 258;
        Write(256, width);
        int current = input[0];
        for (var i = 1; i < input.Length; i++)
        {
            var key = (current << 8) | input[i];
            if (table.TryGetValue(key, out var found))
            {
                current = found;
                continue;
            }

            Write(current, width);
            table[key] = next++;
            if (next >= (1 << width) + early && width < 12)
            {
                width++;
            }

            if (next >= 4093)
            {
                Write(256, width);
                table.Clear();
                width = 9;
                next = 258;
            }

            current = input[i];
        }

        Write(current, width);
        if (next >= (1 << width) - 1 + early && width < 12)
        {
            width++;
        }

        Write(257, width);
        if (count > 0)
        {
            bytes.Add((byte)(bits << (8 - count)));
        }

        return [.. bytes];
    }
}
