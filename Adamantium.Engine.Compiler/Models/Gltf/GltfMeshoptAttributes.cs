using System;
using System.IO;
using System.Numerics;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>Mode 0 of <c>KHR_meshopt_compression</c> (Appendix A), and of <c>EXT_meshopt_compression</c> as its version
/// 0: elements in blocks, each byte of an element stored as deltas from the element before - groups of 16 deltas packed
/// in 1, 2 or 4 bits with a sentinel for those that do not fit, or whole, or all zero - and turned back into values by
/// the channel modes of version 1: bytes, 16-bit values, or 32-bit values XORed and rotated.</summary>
internal static class GltfMeshoptAttributes
{
    private const int GroupSize = 16;

    /// <summary>The fewest bytes a stream of <paramref name="count"/> elements of <paramref name="stride"/> bytes takes:
    /// its header, the control bytes or data block headers of each block, and its tail.</summary>
    public static long MinimumSize(ReadOnlySpan<byte> data, int count, int stride)
    {
        var version = data.IsEmpty ? 1 : data[0] & 15;
        var blocks = (count + (long)MaxBlockElements(stride) - 1) / MaxBlockElements(stride);
        return 1 + blocks * (version == 0 ? stride : stride / 4) + TailSize(version, stride);
    }

    /// <summary>Unpacks <paramref name="count"/> elements of <paramref name="stride"/> bytes from
    /// <paramref name="data"/> into <paramref name="output"/>.</summary>
    public static void Decode(ReadOnlySpan<byte> data, Span<byte> output, int count, int stride)
    {
        var version = data[0] switch
        {
            0xA0 => 0,
            0xA1 => 1,
            _ => throw new InvalidDataException($"meshopt attributes of header {data[0]:X2}.")
        };
        var tailSize = TailSize(version, stride);
        if (data.Length < 1 + tailSize)
        {
            throw Truncated();
        }

        var tail = data[^(stride + (version == 1 ? stride / 4 : 0))..];
        var previous = tail[..stride].ToArray();
        var channels = version == 1 ? tail[stride..] : default;
        if (version == 1)
        {
            foreach (var channel in channels)
            {
                if ((channel & 15) > 2 || (channel & 15) < 2 && channel >> 4 != 0)
                {
                    throw new InvalidDataException($"meshopt attributes of channel mode {channel:X2}.");
                }
            }
        }

        var end = data.Length - tailSize;
        var at = 1;
        var limit = MaxBlockElements(stride);
        var deltas = new byte[stride * limit];
        for (var first = 0; first < count; first += limit)
        {
            var elements = Math.Min(limit, count - first);
            var groups = (elements + GroupSize - 1) / GroupSize;
            var controls = default(ReadOnlySpan<byte>);
            if (version == 1)
            {
                Need(data, at + stride / 4, end);
                controls = data.Slice(at, stride / 4);
                at += stride / 4;
            }

            for (var k = 0; k < stride; k++)
            {
                var column = deltas.AsSpan(k * limit, groups * GroupSize);
                var control = version == 1 ? (controls[k / 4] >> (k % 4 * 2)) & 3 : -1;
                at = DecodeColumn(data, at, end, column, groups, elements, control);
            }

            Restore(deltas, limit, output, first, elements, stride, previous, channels);
        }

        if (at != end)
        {
            throw new InvalidDataException("meshopt attributes leave bytes before their tail.");
        }
    }

    private static int TailSize(int version, int stride) =>
        Math.Max(version == 0 ? 32 : 24, stride + (version == 1 ? stride / 4 : 0));

    private static int MaxBlockElements(int stride) => Math.Min((8192 / stride) & ~15, 256);

    private static int DecodeColumn(ReadOnlySpan<byte> data, int at, int end, Span<byte> column, int groups,
        int elements, int control)
    {
        switch (control)
        {
            case 2:
                column.Clear();
                return at;
            case 3:
                Need(data, at + elements, end);
                data.Slice(at, elements).CopyTo(column);
                return at + elements;
        }

        var headers = (groups + 3) / 4;
        Need(data, at + headers, end);
        var header = data.Slice(at, headers);
        at += headers;
        for (var g = 0; g < groups; g++)
        {
            var mode = (header[g / 4] >> (g % 4 * 2)) & 3;
            var bits = control switch
            {
                -1 => mode switch { 0 => 0, 1 => 2, 2 => 4, _ => 8 },
                0 => mode switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 },
                _ => mode switch { 0 => 1, 1 => 2, 2 => 4, _ => 8 }
            };
            at = DecodeGroup(data, at, end, column.Slice(g * GroupSize, GroupSize), bits);
        }

        return at;
    }

    private static int DecodeGroup(ReadOnlySpan<byte> data, int at, int end, Span<byte> group, int bits)
    {
        if (bits == 0)
        {
            group.Clear();
            return at;
        }

        var packed = GroupSize * bits / 8;
        Need(data, at + packed, end);
        if (bits == 8)
        {
            data.Slice(at, GroupSize).CopyTo(group);
            return at + GroupSize;
        }

        var sentinel = (1 << bits) - 1;
        var perByte = 8 / bits;
        var extra = at + packed;
        for (var i = 0; i < GroupSize; i++)
        {
            var value = data[at + i / perByte];
            var shift = bits == 1 ? i % 8 : 8 - bits * (i % perByte + 1);
            var delta = (value >> shift) & sentinel;
            if (delta == sentinel)
            {
                Need(data, extra + 1, end);
                delta = data[extra++];
            }

            group[i] = (byte)delta;
        }

        return extra;
    }

    private static void Restore(byte[] deltas, int limit, Span<byte> output, int first, int elements, int stride,
        byte[] previous, ReadOnlySpan<byte> channels)
    {
        for (var e = 0; e < elements; e++)
        {
            var element = output.Slice((first + e) * stride, stride);
            for (var c = 0; c < stride; c += 4)
            {
                var mode = channels.IsEmpty ? 0 : channels[c / 4];
                switch (mode & 15)
                {
                    case 0:
                        for (var k = c; k < c + 4; k++)
                        {
                            previous[k] = (byte)(previous[k] + Unzigzag(deltas[k * limit + e]));
                        }

                        break;
                    case 1:
                        for (var k = c; k < c + 4; k += 2)
                        {
                            var delta = (ushort)(deltas[k * limit + e] | (deltas[(k + 1) * limit + e] << 8));
                            var value = (ushort)((previous[k] | (previous[k + 1] << 8)) + Unzigzag(delta));
                            (previous[k], previous[k + 1]) = ((byte)value, (byte)(value >> 8));
                        }

                        break;
                    default:
                    {
                        var delta = (uint)(deltas[c * limit + e] | (deltas[(c + 1) * limit + e] << 8)
                                           | (deltas[(c + 2) * limit + e] << 16) | (deltas[(c + 3) * limit + e] << 24));
                        var value = (uint)(previous[c] | (previous[c + 1] << 8) | (previous[c + 2] << 16)
                                           | (previous[c + 3] << 24)) ^ BitOperations.RotateRight(delta, mode >> 4);
                        (previous[c], previous[c + 1], previous[c + 2], previous[c + 3]) =
                            ((byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24));
                        break;
                    }
                }
            }

            previous.CopyTo(element);
        }
    }

    private static int Unzigzag(byte value) => (value & 1) != 0 ? ~(value >> 1) : value >> 1;

    private static int Unzigzag(ushort value) => (value & 1) != 0 ? ~(value >> 1) : value >> 1;

    private static void Need(ReadOnlySpan<byte> data, int upTo, int end)
    {
        if (upTo > end)
        {
            throw Truncated();
        }
    }

    private static InvalidDataException Truncated() => new("meshopt attributes end too early.");
}
