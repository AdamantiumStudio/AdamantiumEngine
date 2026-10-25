using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
        // A code's string is already in the output - its prefix's string where that was written, and one byte more -
        // so the table keeps where and how long, and a code is copied out whole.
        var oldStyle = input.Length >= 2 && input[0] == 0 && (input[1] & 1) != 0;
        Span<int> start = stackalloc int[TableSize];
        Span<int> length = stackalloc int[TableSize];
        length[..256].Fill(1);
        ulong bits = 0;
        var count = 0;
        var position = 0;
        var written = 0;
        var width = 9;
        var next = FirstFree;
        var old = -1;
        var oldAt = 0;
        while (written < output.Length)
        {
            if (count < width && !Refill(input, oldStyle, width, ref bits, ref count, ref position))
            {
                break;
            }

            count -= width;
            int code;
            if (oldStyle)
            {
                code = (int)(bits & ((1UL << width) - 1));
                bits >>= width;
            }
            else
            {
                code = (int)((bits >> count) & ((1UL << width) - 1));
            }

            if (code == EndOfInformation)
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

            var at = written;
            if (code < 256)
            {
                output[written++] = (byte)code;
            }
            else if (old < 0 || code > next || code < FirstFree)
            {
                break;
            }
            else if (code < next)
            {
                written += Copy(output, start[code], length[code], written);
            }
            else
            {
                var copied = Copy(output, oldAt, length[old], written);
                written += copied;
                if (copied < length[old] || written == output.Length)
                {
                    break;
                }

                output[written++] = output[oldAt];
            }

            if (old >= 0 && next < TableSize)
            {
                start[next] = oldAt;
                length[next] = length[old] + 1;
                next++;
            }

            if (width < 12 && next >= (1 << width) - (oldStyle ? 0 : 1))
            {
                width++;
            }

            old = code;
            oldAt = at;
        }

        return written;
    }

    // Brings at least width bits into bits, most significant first or, for old-style codes, least; false when the
    // input ends first.
    private static bool Refill(ReadOnlySpan<byte> input, bool leastSignificantFirst, int width, ref ulong bits,
        ref int count, ref int position)
    {
        if (position + 8 <= input.Length)
        {
            var take = Math.Min(7, (64 - count) / 8);
            bits = leastSignificantFirst
                ? bits | ((BinaryPrimitives.ReadUInt64LittleEndian(input[position..]) & ((1UL << (take * 8)) - 1))
                          << count)
                : (bits << (take * 8)) | (BinaryPrimitives.ReadUInt64BigEndian(input[position..]) >> (64 - take * 8));
            position += take;
            count += take * 8;
            return true;
        }

        while (count < width)
        {
            if (position == input.Length)
            {
                return false;
            }

            bits = leastSignificantFirst ? bits | ((ulong)input[position++] << count) : (bits << 8) | input[position++];
            count += 8;
        }

        return true;
    }

    // Bytes past count may be written too, when there is room: the codes after overwrite them, or the caller clears
    // what is past the end.
    private static int Copy(Span<byte> output, int from, int count, int to)
    {
        count = Math.Min(count, output.Length - to);
        if (count <= 16 && to + 16 <= output.Length)
        {
            ref var start = ref MemoryMarshal.GetReference(output);
            Unsafe.WriteUnaligned(ref Unsafe.Add(ref start, to),
                Unsafe.ReadUnaligned<Vector128<byte>>(ref Unsafe.Add(ref start, from)));
        }
        else if (count <= 16)
        {
            for (var i = 0; i < count; i++)
            {
                output[to + i] = output[from + i];
            }
        }
        else
        {
            output.Slice(from, count).CopyTo(output[to..]);
        }

        return count;
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
