using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Adamantium.ImagingTests;

/// <summary>Builds small little-endian TIFF files field by field, for what no tool writes: odd bit depths, hostile
/// counts and offsets. The pixel data follows the header; a StripOffsets or TileOffsets value of
/// <see cref="Data"/> points at it.</summary>
public static class HandMadeTiff
{
    /// <summary>The offset of the pixel data, right after the header.</summary>
    public const ulong Data = 8;

    /// <summary>A type that makes a field point elsewhere: its values are [type, count, offset].</summary>
    public const ushort Pointer = 0x8000;

    /// <summary>A file of <paramref name="data"/> and <paramref name="fields"/> (tag, type, values), the directory's
    /// next offset <paramref name="next"/>.</summary>
    public static byte[] Build(byte[] data, (ushort Tag, ushort Type, ulong[] Values)[] fields, uint next = 0)
    {
        var directoryAt = 8 + data.Length + (data.Length & 1);
        var sorted = fields.OrderBy(field => field.Tag).ToArray();
        var tableBytes = 2 + sorted.Length * 12 + 4;
        List<byte> file = [0x49, 0x49, 42, 0, .. Le32((uint)directoryAt), .. data];
        if ((data.Length & 1) != 0)
        {
            file.Add(0);
        }

        List<byte> table = [.. Le16((ushort)sorted.Length)];
        List<byte> values = [];
        foreach (var (tag, type, entries) in sorted)
        {
            if (type == Pointer)
            {
                table.AddRange([.. Le16(tag), .. Le16((ushort)entries[0]), .. Le32((uint)entries[1]),
                    .. Le32((uint)entries[2])]);
                continue;
            }

            var size = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 5 or 9 or 10 or 11 => 4, _ => 8 };
            var bytes = entries.SelectMany(value => size switch
            {
                1 => [(byte)value],
                2 => Le16((ushort)value),
                4 => Le32((uint)value),
                _ => Le64(value)
            }).ToArray();
            var count = type is 5 or 10 ? entries.Length / 2 : entries.Length;
            table.AddRange([.. Le16(tag), .. Le16(type), .. Le32((uint)count)]);
            if (bytes.Length <= 4)
            {
                table.AddRange([.. bytes, .. new byte[4 - bytes.Length]]);
                continue;
            }

            table.AddRange(Le32((uint)(directoryAt + tableBytes + values.Count)));
            values.AddRange(bytes);
        }

        table.AddRange(Le32(next));
        return [.. file, .. table, .. values];
    }

    private static byte[] Le16(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Le32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Le64(ulong value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return bytes;
    }
}
