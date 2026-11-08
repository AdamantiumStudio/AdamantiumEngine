using System;
using System.Collections.Generic;
using Adamantium.Core.Compression;

namespace Adamantium.Imaging.Tiff;

/// <summary>TIFF's LZW (TIFF 6.0, section 13): codes of 9 to 12 bits, most significant bit first, the width growing one
/// code early. Files of TIFF 5.0 writers, least significant bit first without the early change, are read too.</summary>
internal static class TiffLzw
{
    private const int EncoderLimit = 4093;

    /// <summary>Unpacks <paramref name="input"/> into <paramref name="output"/>; returns the bytes written. Stops at
    /// the end code, a full output or the end of the input.</summary>
    public static int Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var oldStyle = input.Length >= 2 && input[0] == 0 && (input[1] & 1) != 0;
        return Lzw.Decode(input, output, !oldStyle, oldStyle);
    }

    /// <summary>Packs <paramref name="input"/> - one strip or tile - into new-style TIFF LZW.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> input)
    {
        var writer = new BitWriter(input.Length / 2 + 16);
        var table = new Dictionary<int, int>();
        var width = 9;
        var next = Lzw.FirstFree;
        writer.Write(Lzw.Clear, width);
        if (input.IsEmpty)
        {
            writer.Write(Lzw.EndOfInformation, width);
            return writer.ToArray();
        }

        int current = input[0];
        for (var i = 1; i < input.Length; i++)
        {
            var key = (current << 8) | input[i];
            if (table.TryGetValue(key, out var found))
            {
                current = found;
                continue;
            }

            writer.Write(current, width);
            table[key] = next++;
            if (next >= 1 << width && width < 12)
            {
                width++;
            }

            if (next >= EncoderLimit)
            {
                writer.Write(Lzw.Clear, width);
                table.Clear();
                width = 9;
                next = Lzw.FirstFree;
            }

            current = input[i];
        }

        writer.Write(current, width);
        if (next >= (1 << width) - 1 && width < 12)
        {
            width++;
        }

        writer.Write(Lzw.EndOfInformation, width);
        return writer.ToArray();
    }

    private sealed class BitWriter
    {
        private readonly List<byte> _bytes;
        private ulong _bits;
        private int _count;

        public BitWriter(int capacity)
        {
            _bytes = new List<byte>(capacity);
        }

        public void Write(int code, int width)
        {
            _bits = (_bits << width) | (uint)code;
            _count += width;
            while (_count >= 8)
            {
                _count -= 8;
                _bytes.Add((byte)(_bits >> _count));
            }
        }

        public byte[] ToArray()
        {
            if (_count > 0)
            {
                _bytes.Add((byte)(_bits << (8 - _count)));
                _count = 0;
            }

            return [.. _bytes];
        }
    }
}
