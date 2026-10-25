using System;
using System.Collections.Generic;

namespace Adamantium.Imaging.Tiff;

/// <summary>TIFF's LZW (TIFF 6.0, section 13): codes of 9 to 12 bits, most significant bit first, the width growing one
/// code early. Files of TIFF 5.0 writers, least significant bit first without the early change, are read too.</summary>
internal static class TiffLzw
{
    private const int Clear = 256;
    private const int EndOfInformation = 257;
    private const int FirstFree = 258;
    private const int TableSize = 4096;
    private const int EncoderLimit = 4093;

    /// <summary>Unpacks <paramref name="input"/> into <paramref name="output"/>; returns the bytes written. Stops at
    /// the end code, a full output or the end of the input.</summary>
    public static int Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var oldStyle = input.Length >= 2 && input[0] == 0 && (input[1] & 1) != 0;
        Span<short> prefix = stackalloc short[TableSize];
        Span<byte> suffix = stackalloc byte[TableSize];
        Span<short> length = stackalloc short[TableSize];
        for (var i = 0; i < 256; i++)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
            length[i] = 1;
        }

        var reader = new BitReader(input, oldStyle);
        var written = 0;
        var width = 9;
        var next = FirstFree;
        var old = -1;
        while (written < output.Length)
        {
            var code = reader.Read(width);
            if (code < 0 || code == EndOfInformation)
            {
                break;
            }

            if (code == Clear)
            {
                width = 9;
                next = FirstFree;
                old = -1;
                continue;
            }

            if (old < 0)
            {
                if (code >= FirstFree)
                {
                    break;
                }

                output[written++] = (byte)code;
                old = code;
                continue;
            }

            int first;
            if (code < next)
            {
                written += Emit(code, prefix, suffix, length, output[written..], out first);
            }
            else if (code == next)
            {
                var copied = Emit(old, prefix, suffix, length, output[written..], out first);
                written += copied;
                if (written < output.Length)
                {
                    output[written++] = (byte)first;
                }
            }
            else
            {
                break;
            }

            if (next < TableSize)
            {
                prefix[next] = (short)old;
                suffix[next] = (byte)first;
                length[next] = (short)Math.Min(length[old] + 1, short.MaxValue);
                next++;
            }

            if (width < 12 && next >= (1 << width) - (oldStyle ? 0 : 1))
            {
                width++;
            }

            old = code;
        }

        return written;
    }

    /// <summary>Packs <paramref name="input"/> - one strip or tile - into new-style TIFF LZW.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> input)
    {
        var writer = new BitWriter(input.Length / 2 + 16);
        var table = new Dictionary<int, int>();
        var width = 9;
        var next = FirstFree;
        writer.Write(Clear, width);
        if (input.IsEmpty)
        {
            writer.Write(EndOfInformation, width);
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
                writer.Write(Clear, width);
                table.Clear();
                width = 9;
                next = FirstFree;
            }

            current = input[i];
        }

        writer.Write(current, width);
        if (next >= (1 << width) - 1 && width < 12)
        {
            width++;
        }

        writer.Write(EndOfInformation, width);
        return writer.ToArray();
    }

    private static int Emit(int code, Span<short> prefix, Span<byte> suffix, Span<short> length, Span<byte> output,
        out int first)
    {
        var count = length[code];
        for (var at = count - 1; code >= 0; at--)
        {
            if (at < output.Length)
            {
                output[at] = suffix[code];
            }

            first = suffix[code];
            code = prefix[code];
            if (code < 0)
            {
                return Math.Min(count, output.Length);
            }
        }

        first = 0;
        return 0;
    }

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _input;
        private readonly bool _leastSignificantFirst;
        private int _position;
        private ulong _bits;
        private int _count;

        public BitReader(ReadOnlySpan<byte> input, bool leastSignificantFirst)
        {
            _input = input;
            _leastSignificantFirst = leastSignificantFirst;
        }

        public int Read(int width)
        {
            while (_count < width)
            {
                if (_position == _input.Length)
                {
                    return -1;
                }

                _bits = _leastSignificantFirst
                    ? _bits | ((ulong)_input[_position++] << _count)
                    : (_bits << 8) | _input[_position++];
                _count += 8;
            }

            _count -= width;
            if (_leastSignificantFirst)
            {
                var value = (int)(_bits & ((1UL << width) - 1));
                _bits >>= width;
                return value;
            }

            return (int)((_bits >> _count) & ((1UL << width) - 1));
        }
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
