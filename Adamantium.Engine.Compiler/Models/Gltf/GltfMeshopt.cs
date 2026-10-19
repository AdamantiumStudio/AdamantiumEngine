// Follows meshoptimizer's decoders (src/vertexcodec.cpp, indexcodec.cpp, vertexfilter.cpp).
// Copyright (c) 2016-2026 Arseny Kapoulkine. Under the MIT licence, details: THIRD-PARTY-NOTICES.md.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>Unpacks buffer views compressed by <c>EXT_meshopt_compression</c> or <c>KHR_meshopt_compression</c>: the
/// attribute codec of versions 0 and 1, the triangle and index codecs, and the octahedral, quaternion, exponential
/// and color filters.</summary>
public static class GltfMeshopt
{
    private const int GroupSize = 16;
    private const int BlockBytes = 8192;
    private const int BlockMaxElements = 256;
    private const int TriangleTableSize = 16;
    private const int IndexTailSize = 4;

    private static readonly int[] BitsV0 = [0, 2, 4, 8];
    private static readonly int[] BitsV1 = [0, 1, 2, 4, 8];

    /// <summary>The <paramref name="compression"/>'s <c>count</c> elements unpacked from <paramref name="data"/>,
    /// the bytes it points at.</summary>
    /// <exception cref="InvalidDataException">The stream is broken or does not fit its description.</exception>
    public static byte[] Decode(GltfMeshoptCompression compression, ReadOnlySpan<byte> data)
    {
        var count = compression.Count;
        var stride = compression.ByteStride;
        var mode = compression.Mode;
        var unfiltered = compression.Filter is null or "NONE";
        var valid = count >= 0 && (long)count * stride <= Array.MaxLength && mode switch
        {
            "ATTRIBUTES" => stride is >= 4 and <= 256 && stride % 4 == 0 && FilterFits(compression.Filter, stride),
            "TRIANGLES" => count % 3 == 0 && stride is 2 or 4 && unfiltered,
            "INDICES" => stride is 2 or 4 && unfiltered,
            _ => false
        };
        if (!valid)
        {
            throw new InvalidDataException($"meshopt mode {mode ?? "none"}, filter {compression.Filter}, " +
                                           $"over {count} elements of {stride} bytes");
        }

        if (data.Length < MinimumSize(mode, data, count, stride))
        {
            throw Truncated();
        }

        var output = new byte[count * stride];
        switch (mode)
        {
            case "ATTRIBUTES":
                DecodeAttributes(data, output, count, stride);
                Filter(compression.Filter, output, stride);
                break;
            case "TRIANGLES":
                DecodeTriangles(data, output, count, stride);
                break;
            default:
                DecodeIndices(data, output, count, stride);
                break;
        }

        return output;
    }

    private static bool FilterFits(string filter, int stride) => filter switch
    {
        null or "NONE" or "EXPONENTIAL" => true,
        "OCTAHEDRAL" or "COLOR" => stride is 4 or 8,
        "QUATERNION" => stride == 8,
        _ => false
    };

    private static long MinimumSize(string mode, ReadOnlySpan<byte> data, int count, int stride)
    {
        switch (mode)
        {
            case "TRIANGLES":
                return 1L + count / 3 + TriangleTableSize;
            case "INDICES":
                return 1L + count + IndexTailSize;
        }

        var version = data.Length > 0 ? data[0] & 0x0F : 0;
        var blockElements = Math.Min((BlockBytes / stride) & ~(GroupSize - 1), BlockMaxElements);
        var blocks = ((long)count + blockElements - 1) / blockElements;
        return 1 + blocks * (version == 0 ? stride : stride / 4) + Math.Max(stride, 24);
    }

    private static void DecodeAttributes(ReadOnlySpan<byte> data, Span<byte> output, int count, int stride)
    {
        if (data.Length < 1 || (data[0] & 0xF0) != 0xA0 || (data[0] & 0x0F) > 1)
        {
            throw new InvalidDataException("meshopt attributes have no header of version 0 or 1");
        }

        var version = data[0] & 0x0F;
        var tailSize = stride + (version == 0 ? 0 : stride / 4);
        var end = data.Length - Math.Max(tailSize, version == 0 ? 32 : 24);
        if (end < 1)
        {
            throw Truncated();
        }

        var tail = data[^tailSize..];
        Span<byte> last = stackalloc byte[stride];
        tail[..stride].CopyTo(last);
        var channels = tail[stride..];
        var deltas = new byte[BlockMaxElements * 4];
        var blockElements = Math.Min((BlockBytes / stride) & ~(GroupSize - 1), BlockMaxElements);
        var at = 1;
        for (var first = 0; first < count; first += blockElements)
        {
            var elements = Math.Min(blockElements, count - first);
            var block = output.Slice(first * stride, elements * stride);
            at = DecodeBlock(data, at, end, version, block, elements, stride, last, channels, deltas);
            block[^stride..].CopyTo(last);
        }

        if (at != end)
        {
            throw new InvalidDataException("meshopt attributes leave bytes before their tail");
        }
    }

    private static int DecodeBlock(ReadOnlySpan<byte> data, int at, int end, int version, Span<byte> block,
        int elements, int stride, ReadOnlySpan<byte> last, ReadOnlySpan<byte> channels, byte[] deltas)
    {
        var aligned = (elements + GroupSize - 1) & ~(GroupSize - 1);
        var control = at;
        at += version == 0 ? 0 : stride / 4;
        Need(at, end);
        for (var k = 0; k < stride; k += 4)
        {
            var controlByte = version == 0 ? 0 : data[control + k / 4];
            for (var j = 0; j < 4; j++)
            {
                var into = deltas.AsSpan(j * elements);
                var mode = (controlByte >> (j * 2)) & 3;
                if (mode == 3)
                {
                    Need(at + elements, end);
                    data.Slice(at, elements).CopyTo(into);
                    at += elements;
                }
                else if (mode == 2)
                {
                    into[..elements].Clear();
                }
                else
                {
                    var bits = version == 0 ? BitsV0.AsSpan() : BitsV1.AsSpan(mode);
                    at = DecodeBytes(data, at, end, into[..aligned], bits);
                }
            }

            Undelta(deltas, block, elements, stride, k, last, version == 0 ? 0 : channels[k / 4]);
        }

        return at;
    }

    private static int DecodeBytes(ReadOnlySpan<byte> data, int at, int end, Span<byte> into, ReadOnlySpan<int> bits)
    {
        var groups = into.Length / GroupSize;
        var header = at;
        at += (groups + 3) / 4;
        Need(at, end);
        for (var g = 0; g < groups; g++)
        {
            var mode = (data[header + g / 4] >> (g % 4 * 2)) & 3;
            at = DecodeGroup(data, at, end, into.Slice(g * GroupSize, GroupSize), bits[mode]);
        }

        return at;
    }

    private static int DecodeGroup(ReadOnlySpan<byte> data, int at, int end, Span<byte> into, int bits)
    {
        if (bits == 0)
        {
            into.Clear();
            return at;
        }

        var packed = GroupSize * bits / 8;
        Need(at + packed, end);
        if (bits == 8)
        {
            data.Slice(at, GroupSize).CopyTo(into);
            return at + GroupSize;
        }

        var sentinel = (1 << bits) - 1;
        var perByte = 8 / bits;
        var extra = at + packed;
        for (var i = 0; i < GroupSize; i++)
        {
            var shift = bits == 1 ? i % 8 : 8 - bits * (i % perByte + 1);
            var delta = (data[at + i / perByte] >> shift) & sentinel;
            if (delta == sentinel)
            {
                Need(extra + 1, end);
                delta = data[extra++];
            }

            into[i] = (byte)delta;
        }

        return extra;
    }

    private static void Undelta(byte[] deltas, Span<byte> block, int elements, int stride, int k,
        ReadOnlySpan<byte> last, int channel)
    {
        var width = (channel & 3) switch
        {
            0 => 1,
            1 => 2,
            2 => 4,
            _ => throw new InvalidDataException($"a meshopt channel mode of {channel & 3}")
        };
        var rotation = (32 - (channel >> 4)) & 31;
        var mask = width == 4 ? uint.MaxValue : (1u << (width * 8)) - 1;
        for (var part = 0; part < 4; part += width)
        {
            var previous = Gather(last, k + part, 1, width);
            for (var i = 0; i < elements; i++)
            {
                var value = Gather(deltas, part * elements + i, elements, width);
                value = width == 4
                    ? BitOperations.RotateLeft(value, rotation) ^ previous
                    : (((0u - (value & 1)) ^ (value >> 1)) + previous) & mask;
                for (var j = 0; j < width; j++)
                {
                    block[i * stride + k + part + j] = (byte)(value >> (j * 8));
                }

                previous = value;
            }
        }
    }

    private static uint Gather(ReadOnlySpan<byte> bytes, int at, int step, int width)
    {
        var value = 0u;
        for (var j = 0; j < width; j++)
        {
            value |= (uint)bytes[at + j * step] << (j * 8);
        }

        return value;
    }

    private static void DecodeTriangles(ReadOnlySpan<byte> data, Span<byte> output, int count, int stride)
    {
        var triangles = count / 3;
        if (data.Length < 1 + triangles + TriangleTableSize || (data[0] & 0xF0) != 0xE0 || (data[0] & 0x0F) > 1)
        {
            throw new InvalidDataException("meshopt triangles have no header of version 0 or 1");
        }

        var lastCached = (data[0] & 0x0F) == 0 ? 15 : 13;
        var end = data.Length - TriangleTableSize;
        var table = data[end..];
        Span<uint> edges = stackalloc uint[32];
        Span<uint> vertices = stackalloc uint[16];
        edges.Fill(uint.MaxValue);
        vertices.Fill(uint.MaxValue);
        var edgeAt = 0;
        var vertexAt = 0;
        var next = 0u;
        var last = 0u;
        var at = 1 + triangles;
        for (var t = 0; t < triangles; t++)
        {
            var code = data[1 + t];
            uint a;
            uint b;
            uint c;
            if (code < 0xF0)
            {
                var edge = (edgeAt - 1 - (code >> 4)) & 15;
                a = edges[edge * 2];
                b = edges[edge * 2 + 1];
                var third = code & 15;
                if (third == 0)
                {
                    c = next++;
                    Push(vertices, ref vertexAt, c);
                }
                else if (third < lastCached)
                {
                    c = vertices[(vertexAt - 1 - third) & 15];
                }
                else
                {
                    c = last = third == 15 ? ReadIndex(data, ref at, end, last) : last + (uint)(third * 2 - 27);
                    Push(vertices, ref vertexAt, c);
                }

                PushEdge(edges, ref edgeAt, c, b);
                PushEdge(edges, ref edgeAt, a, c);
            }
            else
            {
                var explicitData = code >= 0xFE;
                byte aux;
                if (explicitData)
                {
                    Need(at + 1, end);
                    aux = data[at++];
                    if (aux == 0)
                    {
                        next = 0;
                    }
                }
                else
                {
                    aux = table[code & 15];
                }

                var second = aux >> 4;
                var third = aux & 15;
                a = code == 0xFF ? 0 : next++;
                b = second == 0 ? next++ : vertices[(vertexAt - second) & 15];
                c = third == 0 ? next++ : vertices[(vertexAt - third) & 15];
                if (code == 0xFF)
                {
                    a = last = ReadIndex(data, ref at, end, last);
                }

                if (explicitData && second == 15)
                {
                    b = last = ReadIndex(data, ref at, end, last);
                }

                if (explicitData && third == 15)
                {
                    c = last = ReadIndex(data, ref at, end, last);
                }

                Push(vertices, ref vertexAt, a);
                if (second is 0 or 15)
                {
                    Push(vertices, ref vertexAt, b);
                }

                if (third is 0 or 15)
                {
                    Push(vertices, ref vertexAt, c);
                }

                PushEdge(edges, ref edgeAt, b, a);
                PushEdge(edges, ref edgeAt, c, b);
                PushEdge(edges, ref edgeAt, a, c);
            }

            Write(output, t * 3, stride, a);
            Write(output, t * 3 + 1, stride, b);
            Write(output, t * 3 + 2, stride, c);
        }

        if (at != end)
        {
            throw new InvalidDataException("meshopt triangles leave bytes before their table");
        }
    }

    private static void DecodeIndices(ReadOnlySpan<byte> data, Span<byte> output, int count, int stride)
    {
        if (data.Length < 1 + count + IndexTailSize || (data[0] & 0xF0) != 0xD0 || (data[0] & 0x0F) > 1)
        {
            throw new InvalidDataException("meshopt indices have no header of version 0 or 1");
        }

        var end = data.Length - IndexTailSize;
        Span<uint> last = stackalloc uint[2];
        var at = 1;
        for (var i = 0; i < count; i++)
        {
            var value = ReadVarint(data, ref at, end);
            var baseline = (int)(value & 1);
            value >>= 1;
            last[baseline] += (value >> 1) ^ (0u - (value & 1));
            Write(output, i, stride, last[baseline]);
        }

        if (at != end)
        {
            throw new InvalidDataException("meshopt indices leave bytes before their tail");
        }
    }

    private static void Push(Span<uint> vertices, ref int at, uint vertex)
    {
        vertices[at] = vertex;
        at = (at + 1) & 15;
    }

    private static void PushEdge(Span<uint> edges, ref int at, uint a, uint b)
    {
        edges[at * 2] = a;
        edges[at * 2 + 1] = b;
        at = (at + 1) & 15;
    }

    private static uint ReadIndex(ReadOnlySpan<byte> data, ref int at, int end, uint last)
    {
        var value = ReadVarint(data, ref at, end);
        return last + ((value >> 1) ^ (0u - (value & 1)));
    }

    private static uint ReadVarint(ReadOnlySpan<byte> data, ref int at, int end)
    {
        Need(at + 1, end);
        var lead = data[at++];
        if (lead < 128)
        {
            return lead;
        }

        var value = lead & 127u;
        var shift = 7;
        for (var i = 0; i < 4; i++)
        {
            Need(at + 1, end);
            var group = data[at++];
            value |= (uint)(group & 127) << shift;
            shift += 7;
            if (group < 128)
            {
                break;
            }
        }

        return value;
    }

    private static void Write(Span<byte> output, int index, int stride, uint value)
    {
        if (stride == 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(output[(index * 2)..], (ushort)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(output[(index * 4)..], value);
        }
    }

    private static void Filter(string filter, Span<byte> output, int stride)
    {
        switch (filter)
        {
            case "OCTAHEDRAL":
                Octahedral(output, stride);
                break;
            case "QUATERNION":
                Quaternion(output);
                break;
            case "EXPONENTIAL":
                Exponential(output);
                break;
            case "COLOR":
                Color(output, stride);
                break;
        }
    }

    private static void Octahedral(Span<byte> output, int stride)
    {
        var size = stride / 4;
        var max = size == 1 ? 127f : 32767f;
        for (var at = 0; at < output.Length; at += stride)
        {
            var element = output.Slice(at, stride);
            float x = Signed(element, 0, size);
            float y = Signed(element, 1, size);
            var z = Signed(element, 2, size) - MathF.Abs(x) - MathF.Abs(y);
            var t = MathF.Min(z, 0);
            x += x >= 0 ? t : -t;
            y += y >= 0 ? t : -t;
            var scale = max / MathF.Sqrt(x * x + y * y + z * z);
            Store(element, 0, size, Round(x * scale));
            Store(element, 1, size, Round(y * scale));
            Store(element, 2, size, Round(z * scale));
        }
    }

    private static void Quaternion(Span<byte> output)
    {
        var range = 32767f / MathF.Sqrt(2);
        for (var at = 0; at < output.Length; at += 8)
        {
            var element = output.Slice(at, 8);
            var last = Signed(element, 3, 2);
            float one = last | 3;
            float x = Signed(element, 0, 2);
            float y = Signed(element, 1, 2);
            float z = Signed(element, 2, 2);
            var w = MathF.Sqrt(MathF.Max(one * one * 2 - x * x - y * y - z * z, 0));
            var scale = range / one;
            var largest = last & 3;
            Store(element, (largest + 1) & 3, 2, Round(x * scale));
            Store(element, (largest + 2) & 3, 2, Round(y * scale));
            Store(element, (largest + 3) & 3, 2, Round(z * scale));
            Store(element, largest, 2, Round(w * scale));
        }
    }

    private static void Exponential(Span<byte> output)
    {
        for (var at = 0; at < output.Length; at += 4)
        {
            var value = BinaryPrimitives.ReadInt32LittleEndian(output[at..]);
            var mantissa = (value << 8) >> 8;
            BinaryPrimitives.WriteSingleLittleEndian(output[at..], MathF.ScaleB(mantissa, value >> 24));
        }
    }

    private static void Color(Span<byte> output, int stride)
    {
        var size = stride / 4;
        var max = size == 1 ? 255f : 65535f;
        for (var at = 0; at < output.Length; at += stride)
        {
            var element = output.Slice(at, stride);
            var alpha = Unsigned(element, 3, size);
            var alphaMask = alpha | (alpha >> 1);
            alphaMask |= alphaMask >> 2;
            alphaMask |= alphaMask >> 4;
            alphaMask |= alphaMask >> 8;
            var y = Unsigned(element, 0, size);
            var co = Signed(element, 1, size);
            var cg = Signed(element, 2, size);
            var scale = max / alphaMask;
            Store(element, 0, size, (int)((y + co - cg) * scale + 0.5f));
            Store(element, 1, size, (int)((y + cg) * scale + 0.5f));
            Store(element, 2, size, (int)((y - co - cg) * scale + 0.5f));
            Store(element, 3, size, (int)((((alpha << 1) & alphaMask) | (alpha & 1)) * scale + 0.5f));
        }
    }

    private static int Signed(ReadOnlySpan<byte> element, int component, int size) =>
        size == 1 ? (sbyte)element[component] : BinaryPrimitives.ReadInt16LittleEndian(element[(component * 2)..]);

    private static int Unsigned(ReadOnlySpan<byte> element, int component, int size) =>
        size == 1 ? element[component] : BinaryPrimitives.ReadUInt16LittleEndian(element[(component * 2)..]);

    private static void Store(Span<byte> element, int component, int size, int value)
    {
        if (size == 1)
        {
            element[component] = (byte)value;
        }
        else
        {
            BinaryPrimitives.WriteInt16LittleEndian(element[(component * 2)..], (short)value);
        }
    }

    private static int Round(float value) => (int)(value + (value >= 0 ? 0.5f : -0.5f));

    private static void Need(int upTo, int end)
    {
        if (upTo > end)
        {
            throw Truncated();
        }
    }

    private static InvalidDataException Truncated() => new("meshopt data ends too early");
}
