#if NET6_0_OR_GREATER
using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Adamantium.Core.Compression;

/// <summary>LZW as TIFF and PDF store it: codes of 9 to 12 bits, 256 clears the table, 257 ends the data.</summary>
public static class Lzw
{
    /// <summary>The code that clears the table.</summary>
    public const int Clear = 256;

    /// <summary>The code that ends the data.</summary>
    public const int EndOfInformation = 257;

    /// <summary>The first code the table gives out.</summary>
    public const int FirstFree = 258;

    /// <summary>How many codes the table holds.</summary>
    public const int TableSize = 4096;

    /// <summary>Unpacks most-significant-bit-first codes into <paramref name="output"/> and returns the bytes written.
    /// Stops at the end code, a full output or the end of the input; a full output may mean more was left.</summary>
    /// <param name="earlyChange">Whether the code width grows one code early, as TIFF 6.0 and PDF's default do.</param>
    public static int Decode(ReadOnlySpan<byte> input, Span<byte> output, bool earlyChange = true)
    {
        return Decode(input, output, earlyChange, false);
    }

    /// <summary>The same, with the codes' bit order given: least significant bit first is what TIFF 5.0 writers made.
    /// </summary>
    public static int Decode(ReadOnlySpan<byte> input, Span<byte> output, bool earlyChange,
        bool leastSignificantFirst)
    {
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
        var early = earlyChange ? 1 : 0;
        while (written < output.Length)
        {
            if (count < width && !Refill(input, leastSignificantFirst, width, ref bits, ref count, ref position))
            {
                break;
            }

            count -= width;
            int code;
            if (leastSignificantFirst)
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

            if (width < 12 && next >= (1 << width) - early)
            {
                width++;
            }

            old = code;
            oldAt = at;
        }

        return written;
    }

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
}
#endif
