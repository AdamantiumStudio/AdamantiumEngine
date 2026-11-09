using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Adamantium.FontTests;

/// <summary>Takes an sfnt font apart into its tables and puts tables together into one.</summary>
internal static class SfntBuilder
{
    public static Dictionary<string, byte[]> TablesOf(byte[] font)
    {
        var tables = new Dictionary<string, byte[]>();
        var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var record = font.AsSpan(12 + i * 16, 16);
            var tag = Encoding.ASCII.GetString(record[..4]);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            tables[tag] = font.AsSpan(offset, length).ToArray();
        }

        return tables;
    }

    /// <summary>An sfnt of the given tables, each on a four byte boundary.</summary>
    public static byte[] Sfnt(IReadOnlyDictionary<string, byte[]> tables, uint version = 0x00010000)
    {
        var tags = tables.Keys.OrderBy(t => t, StringComparer.Ordinal).ToArray();
        var output = new MemoryStream();
        var header = new byte[12 + tags.Length * 16];
        BinaryPrimitives.WriteUInt32BigEndian(header, version);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)tags.Length);
        var offset = header.Length;
        for (var i = 0; i < tags.Length; i++)
        {
            var record = header.AsSpan(12 + i * 16);
            Encoding.ASCII.GetBytes(tags[i], record);
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)tables[tags[i]].Length);
            offset += (tables[tags[i]].Length + 3) & ~3;
        }

        output.Write(header);
        foreach (var tag in tags)
        {
            output.Write(tables[tag]);
            output.Write(new byte[((tables[tag].Length + 3) & ~3) - tables[tag].Length]);
        }

        return output.ToArray();
    }

    /// <summary>A TrueType font of the given glyf entries: head with long loca offsets, maxp 0.5 and loca.</summary>
    public static byte[] TrueType(IReadOnlyList<byte[]> glyphs)
    {
        var head = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(head, 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(head.AsSpan(18), 1000);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(50), 1);

        var maxp = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(maxp, 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)glyphs.Count);

        var glyf = new MemoryStream();
        var loca = new byte[(glyphs.Count + 1) * 4];
        for (var i = 0; i < glyphs.Count; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), (uint)glyf.Length);
            glyf.Write(glyphs[i]);
            glyf.Write(new byte[glyphs[i].Length % 2]);
        }

        BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(glyphs.Count * 4), (uint)glyf.Length);
        return Sfnt(new Dictionary<string, byte[]>
        {
            ["head"] = head, ["maxp"] = maxp, ["loca"] = loca, ["glyf"] = glyf.ToArray(),
        });
    }

    /// <summary>A glyph of one contour of <paramref name="points"/> points, all at the origin.</summary>
    public static byte[] SimpleGlyph(int points)
    {
        var glyph = new byte[10 + 2 + 2 + points];
        BinaryPrimitives.WriteInt16BigEndian(glyph, 1);
        BinaryPrimitives.WriteUInt16BigEndian(glyph.AsSpan(10), (ushort)(points - 1));
        for (var i = 0; i < points; i++)
        {
            glyph[14 + i] = 0x31;
        }

        return glyph;
    }

    /// <summary>A composite glyph of the given glyphs, each placed at the origin.</summary>
    public static byte[] CompositeGlyph(params int[] components)
    {
        var glyph = new MemoryStream();
        glyph.Write([0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0]);
        for (var i = 0; i < components.Length; i++)
        {
            var flags = (ushort)(0x0001 | 0x0002 | (i < components.Length - 1 ? 0x0020 : 0));
            var entry = new byte[8];
            BinaryPrimitives.WriteUInt16BigEndian(entry, flags);
            BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(2), (ushort)components[i]);
            glyph.Write(entry);
        }

        return glyph.ToArray();
    }
}
