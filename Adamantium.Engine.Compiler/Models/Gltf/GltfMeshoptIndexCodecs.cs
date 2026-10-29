using System;
using System.Buffers.Binary;
using System.IO;

namespace Adamantium.Engine.Compiler.Models.Gltf;

/// <summary>Modes 1 and 2 of <c>KHR_meshopt_compression</c> (Appendix A): triangle lists, each triangle a code byte
/// that names recent edges and vertices, the next new vertex or one beside the last, and extra bytes for the rest; and
/// indices of any kind as deltas from one of two baselines. Indices come out as 16-bit values, wrapped, or 32-bit.
/// </summary>
internal static class GltfMeshoptIndexCodecs
{
    private const int TriangleTail = 16;
    private const int IndexTail = 4;

    /// <summary>The fewest bytes a triangle stream of <paramref name="count"/> indices takes: header, a code a
    /// triangle, and the code table.</summary>
    public static long MinimumTriangleSize(int count) => 1 + count / 3 + TriangleTail;

    /// <summary>The fewest bytes an index stream of <paramref name="count"/> indices takes: header, a byte an index,
    /// and the padding.</summary>
    public static long MinimumIndexSize(int count) => 1 + (long)count + IndexTail;

    /// <summary>Unpacks a triangle list of <paramref name="count"/> indices into <paramref name="output"/>.</summary>
    public static void DecodeTriangles(ReadOnlySpan<byte> data, Span<byte> output, int count, int stride)
    {
        if (data[0] != 0xE1)
        {
            throw new InvalidDataException($"meshopt triangles of header {data[0]:X2}.");
        }

        var triangles = count / 3;
        var end = data.Length - TriangleTail;
        if (end < 1 + triangles)
        {
            throw Truncated();
        }

        var table = data[end..];
        if (table[14] != 0 || table[15] != 0)
        {
            throw new InvalidDataException("A meshopt triangle code table ends in other than zeros.");
        }

        foreach (var entry in table)
        {
            if (entry >> 4 == 15 || (entry & 15) == 15)
            {
                throw new InvalidDataException("A meshopt triangle code table names a vertex past its FIFO.");
            }
        }

        var state = new GltfMeshoptTriangleState();
        var at = 1 + triangles;
        for (var t = 0; t < triangles; t++)
        {
            var code = data[1 + t];
            int high = code >> 4, low = code & 15;
            uint a, b, c;
            if (high < 15)
            {
                (a, b) = state.Edge(high);
                switch (low)
                {
                    case 0:
                        c = state.Next++;
                        state.PushVertex(c);
                        break;
                    case < 13:
                        c = state.Vertex(low);
                        break;
                    case 13 or 14:
                        c = state.Last = low == 13 ? state.Last - 1 : state.Last + 1;
                        state.PushVertex(c);
                        break;
                    default:
                        c = state.Delta(ReadVarint(data, ref at, end));
                        state.PushVertex(c);
                        break;
                }

                state.PushEdge(c, b);
                state.PushEdge(a, c);
            }
            else
            {
                int pair;
                if (low < 14)
                {
                    pair = table[low];
                    a = state.Next++;
                }
                else
                {
                    Need(at + 1, end);
                    pair = data[at++];
                    if (pair == 0)
                    {
                        state.Next = 0;
                    }

                    a = low == 14 ? state.Next++ : state.Delta(ReadVarint(data, ref at, end));
                }

                int z = pair >> 4, w = pair & 15;
                b = Third(state, z, data, ref at, end);
                c = Third(state, w, data, ref at, end);
                state.PushEdge(b, a);
                state.PushEdge(c, b);
                state.PushEdge(a, c);
                state.PushVertex(a);
                if (z is 0 or 15)
                {
                    state.PushVertex(b);
                }

                if (w is 0 or 15)
                {
                    state.PushVertex(c);
                }
            }

            Write(output, 3 * t, stride, a);
            Write(output, 3 * t + 1, stride, b);
            Write(output, 3 * t + 2, stride, c);
        }

        if (at != end)
        {
            throw new InvalidDataException("meshopt triangles leave bytes before their code table.");
        }
    }

    /// <summary>Unpacks <paramref name="count"/> indices into <paramref name="output"/>.</summary>
    public static void DecodeIndices(ReadOnlySpan<byte> data, Span<byte> output, int count, int stride)
    {
        if (data[0] != 0xD1)
        {
            throw new InvalidDataException($"meshopt indices of header {data[0]:X2}.");
        }

        var end = data.Length - IndexTail;
        Span<uint> last = [0, 0];
        var at = 1;
        for (var i = 0; i < count; i++)
        {
            var value = ReadVarint(data, ref at, end);
            var delta = (value & 2) != 0 ? ~(value >> 2) : value >> 2;
            Write(output, i, stride, last[(int)(value & 1)] += delta);
        }

        if (at != end)
        {
            throw new InvalidDataException("meshopt indices leave bytes before their padding.");
        }
    }

    private static uint Third(GltfMeshoptTriangleState state, int selector, ReadOnlySpan<byte> data, ref int at, int end) =>
        selector switch
        {
            0 => state.Next++,
            < 15 => state.Vertex(selector - 1),
            _ => state.Delta(ReadVarint(data, ref at, end))
        };

    private static uint ReadVarint(ReadOnlySpan<byte> data, ref int at, int end)
    {
        uint value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            Need(at + 1, end);
            var part = data[at++];
            value |= (uint)(part & 0x7F) << shift;
            if ((part & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("A meshopt index of more than five bytes.");
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

    private static void Need(int upTo, int end)
    {
        if (upTo > end)
        {
            throw Truncated();
        }
    }

    private static InvalidDataException Truncated() => new("meshopt indices end too early.");
}
