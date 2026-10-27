using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Adamantium.Core.Fonts;

namespace Adamantium.Fonts;

internal static class FontFaceReader
{
    private const int NameFamily = 1;
    private const int NameSubfamily = 2;
    private const int NameFullName = 4;
    private const int NameTypographicFamily = 16;
    private const int NameTypographicSubfamily = 17;

    public static List<FontFace> Read(string path)
    {
        var faces = new List<FontFace>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.RandomAccess);
        var tag = ReadTag(stream, 0);
        if (tag == "ttcf")
        {
            var count = (int)ReadUInt32(stream, 8);
            for (var i = 0; i < count; i++)
            {
                var face = ReadFace(stream, ReadUInt32(stream, 12 + 4 * i), path, i);
                if (face != null)
                {
                    faces.Add(face);
                }
            }
        }
        else if (tag is "true" or "OTTO" || ReadUInt32(stream, 0) == 0x00010000)
        {
            var face = ReadFace(stream, 0, path, 0);
            if (face != null)
            {
                faces.Add(face);
            }
        }

        return faces;
    }

    /// <summary>The characters the font at <paramref name="collectionIndex"/> of the file maps, as sorted ranges, read from
    /// its 'cmap' alone: the best Unicode subtable of format 4, 12 or 13.</summary>
    public static List<(int Start, int End)> ReadCoverage(string path, int collectionIndex)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.RandomAccess);
        long offset = 0;
        if (ReadTag(stream, 0) == "ttcf")
        {
            offset = ReadUInt32(stream, 12 + 4 * collectionIndex);
        }

        long cmap = -1;
        var count = ReadUInt16(stream, offset + 4);
        for (var i = 0; i < count; i++)
        {
            var record = offset + 12 + 16 * i;
            if (ReadTag(stream, record) == "cmap")
            {
                cmap = ReadUInt32(stream, record + 8);
            }
        }

        var ranges = new List<(int Start, int End)>();
        if (cmap < 0)
        {
            return ranges;
        }

        long best = -1;
        var bestRank = -1;
        var subtables = ReadUInt16(stream, cmap + 2);
        for (var i = 0; i < subtables; i++)
        {
            var record = cmap + 4 + 8 * i;
            var platform = ReadUInt16(stream, record);
            var encoding = ReadUInt16(stream, record + 2);
            var subtable = cmap + ReadUInt32(stream, record + 4);
            var format = ReadUInt16(stream, subtable);
            var rank = (platform, encoding, format) switch
            {
                (3, 10, 12) or (0, 4, 12) or (0, 6, 13) => 3,
                (0, _, 12) => 2,
                (3, 1, 4) or (0, 3, 4) => 1,
                (0, _, 4) or (3, 0, 4) => 0,
                _ => -1,
            };
            if (rank > bestRank)
            {
                best = subtable;
                bestRank = rank;
            }
        }

        if (best < 0)
        {
            return ranges;
        }

        if (ReadUInt16(stream, best) == 4)
        {
            var segments = ReadUInt16(stream, best + 6) / 2;
            for (var i = 0; i < segments; i++)
            {
                var end = ReadUInt16(stream, best + 14 + 2 * i);
                var start = ReadUInt16(stream, best + 16 + 2 * segments + 2 * i);
                if (start != 0xFFFF)
                {
                    ranges.Add((start, end));
                }
            }
        }
        else
        {
            var groups = ReadUInt32(stream, best + 12);
            for (var i = 0; i < groups; i++)
            {
                var group = best + 16 + 12 * i;
                ranges.Add(((int)ReadUInt32(stream, group), (int)ReadUInt32(stream, group + 4)));
            }
        }

        ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
        return ranges;
    }

    private static FontFace ReadFace(Stream stream, long offset, string path, int index)
    {
        var tables = new Dictionary<string, long>();
        var count = ReadUInt16(stream, offset + 4);
        for (var i = 0; i < count; i++)
        {
            var record = offset + 12 + 16 * i;
            tables[ReadTag(stream, record)] = ReadUInt32(stream, record + 8);
        }

        if (!tables.TryGetValue("name", out var name))
        {
            return null;
        }

        var names = ReadNames(stream, name);
        names.TryGetValue(NameFamily, out var legacyFamily);
        if (!names.TryGetValue(NameTypographicFamily, out var family))
        {
            family = legacyFamily;
        }

        if (string.IsNullOrEmpty(family))
        {
            return null;
        }

        names.TryGetValue(NameSubfamily, out var subfamily);
        if (!names.TryGetValue(NameTypographicSubfamily, out var faceName))
        {
            faceName = subfamily ?? "Regular";
        }

        names.TryGetValue(NameFullName, out var fullName);

        var weight = FontWeight.Normal;
        var stretch = FontStretch.Normal;
        var style = FontStyle.Normal;
        if (tables.TryGetValue("OS/2", out var os2))
        {
            var weightClass = ReadUInt16(stream, os2 + 4);
            if (weightClass is > 0 and < 10)
            {
                weightClass *= 100;
            }

            weight = new FontWeight(Math.Max(1, Math.Min(1000, (int)weightClass)));
            stretch = FontStretch.FromWidthClass(ReadUInt16(stream, os2 + 6));
            var selection = ReadUInt16(stream, os2 + 62);
            if ((selection & (1 << 9)) != 0)
            {
                style = FontStyle.Oblique;
            }
            else if ((selection & 1) != 0)
            {
                style = FontStyle.Italic;
            }
        }

        if (style == FontStyle.Normal && tables.TryGetValue("head", out var head)
            && (ReadUInt16(stream, head + 44) & 2) != 0)
        {
            style = FontStyle.Italic;
        }

        FontWeight minWeight = default, maxWeight = default;
        FontStretch minStretch = default, maxStretch = default;
        if (tables.TryGetValue("fvar", out var fvar))
        {
            var axesOffset = ReadUInt16(stream, fvar + 4);
            var axisCount = ReadUInt16(stream, fvar + 8);
            var axisSize = ReadUInt16(stream, fvar + 10);
            for (var i = 0; i < axisCount; i++)
            {
                var axis = fvar + axesOffset + axisSize * i;
                var axisTag = ReadTag(stream, axis);
                var min = ReadFixed(stream, axis + 4);
                var max = ReadFixed(stream, axis + 12);
                if (axisTag == "wght" && min >= 1 && max <= 1000 && min <= max)
                {
                    minWeight = new FontWeight((int)Math.Round(min));
                    maxWeight = new FontWeight((int)Math.Round(max));
                }
                else if (axisTag == "wdth" && min > 0 && min <= max)
                {
                    minStretch = new FontStretch(min);
                    maxStretch = new FontStretch(max);
                }
            }
        }

        return new FontFace(family, legacyFamily, faceName, fullName, weight, style, stretch, path, index,
            minWeight, maxWeight, minStretch, maxStretch);
    }

    private static Dictionary<int, string> ReadNames(Stream stream, long table)
    {
        var names = new Dictionary<int, string>();
        var ranks = new Dictionary<int, int>();
        var count = ReadUInt16(stream, table + 2);
        var storage = table + ReadUInt16(stream, table + 4);
        for (var i = 0; i < count; i++)
        {
            var record = table + 6 + 12 * i;
            var platform = ReadUInt16(stream, record);
            var encoding = ReadUInt16(stream, record + 2);
            var language = ReadUInt16(stream, record + 4);
            var id = ReadUInt16(stream, record + 6);
            if (id is not (NameFamily or NameSubfamily or NameFullName or NameTypographicFamily
                or NameTypographicSubfamily))
            {
                continue;
            }

            var rank = platform switch
            {
                3 when encoding is 1 or 10 => language == 0x0409 ? 4 : 3,
                0 => 2,
                1 when encoding == 0 => language == 0 ? 1 : 0,
                _ => -1,
            };
            if (rank < 0 || (ranks.TryGetValue(id, out var best) && best >= rank))
            {
                continue;
            }

            var length = ReadUInt16(stream, record + 8);
            var bytes = ReadBytes(stream, storage + ReadUInt16(stream, record + 10), length);
            names[id] = platform == 1
                ? Encoding.GetEncoding("ISO-8859-1").GetString(bytes)
                : Encoding.BigEndianUnicode.GetString(bytes);
            ranks[id] = rank;
        }

        return names;
    }

    private static byte[] ReadBytes(Stream stream, long position, int length)
    {
        var bytes = new byte[length];
        stream.Position = position;
        var read = 0;
        while (read < length)
        {
            var chunk = stream.Read(bytes, read, length - read);
            if (chunk <= 0)
            {
                throw new EndOfStreamException();
            }

            read += chunk;
        }

        return bytes;
    }

    private static string ReadTag(Stream stream, long position) =>
        Encoding.ASCII.GetString(ReadBytes(stream, position, 4));

    private static ushort ReadUInt16(Stream stream, long position)
    {
        var bytes = ReadBytes(stream, position, 2);
        return (ushort)(bytes[0] << 8 | bytes[1]);
    }

    private static uint ReadUInt32(Stream stream, long position)
    {
        var bytes = ReadBytes(stream, position, 4);
        return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
    }

    private static double ReadFixed(Stream stream, long position) => (int)ReadUInt32(stream, position) / 65536.0;
}
