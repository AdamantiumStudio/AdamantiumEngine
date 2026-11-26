using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Adamantium.Fonts.Subsetting;

/// <summary>A font of the few glyphs a document uses as an OpenType file: the CFF program of <see cref="CffSubset"/> with
/// the tables a reader of OpenType needs beside it (OpenType 1.9: 'head', 'hhea', 'hmtx', 'maxp', 'OS/2', 'name',
/// 'post', 'cmap'). What an XPS document embeds, where a PDF takes the CFF program alone. A glyph of the subset is the
/// place of its index in <see cref="CffSubset.IndicesOf"/>; its widths are its font's, at
/// <see cref="CffSubset.UnitsPerEm"/> units to the em.</summary>
public static class OpenTypeSubset
{
    private const uint ChecksumMagic = 0xB1B0AFBA;

    /// <summary>Writes the glyphs of <paramref name="font"/> at <paramref name="indices"/> as an OpenType font called
    /// <paramref name="name"/>; they are laid out as <see cref="CffSubset.IndicesOf"/> lays them, whatever their order.
    /// Embedded for printing: its 'OS/2' allows preview and print, as a restricted font.</summary>
    public static byte[] Write(IFont font, IEnumerable<uint> indices, string name)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentException.ThrowIfNullOrEmpty(name);
        var laid = CffSubset.IndicesOf(font, indices);
        return Write(font, laid, name, new Metrics(font, laid));
    }

    private static byte[] Write(IFont font, IReadOnlyList<uint> indices, string name, Metrics metrics)
    {
        var tables = new SortedDictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["CFF "] = CffSubset.Write(font, indices, name),
            ["OS/2"] = Os2(metrics),
            ["cmap"] = Cmap(font, indices),
            ["head"] = Head(metrics),
            ["hhea"] = Hhea(metrics),
            ["hmtx"] = Hmtx(metrics),
            ["maxp"] = Maxp(indices.Count),
            ["name"] = Name(CffSubset.Plain(name), Face(font)),
            ["post"] = Post(metrics),
        };
        return File(tables);
    }

    // The widths and boxes of the glyphs written, and the font's lines, at the subset's units.
    private sealed class Metrics
    {
        public Metrics(IFont font, IReadOnlyList<uint> indices)
        {
            Font = font;
            Scale = (double)CffSubset.UnitsPerEm / font.UnitsPerEm;
            Advances = new ushort[indices.Count];
            Bearings = new short[indices.Count];
            short xMin = short.MaxValue, yMin = short.MaxValue, xMax = short.MinValue, yMax = short.MinValue;
            var minRight = int.MaxValue;
            for (var i = 0; i < indices.Count; i++)
            {
                var glyph = font.GetGlyphByIndex(indices[i]);
                Advances[i] = (ushort)Math.Clamp(Math.Round(font.GetAdvanceWidth(indices[i]) * Scale), 0, ushort.MaxValue);
                var box = glyph.BoundingRectangle;
                if (!glyph.HasOutlines || !(box.Width > 0) || !(box.Height > 0))
                {
                    continue;
                }

                var left = Em(box.X);
                var right = Em(box.X + box.Width);
                Bearings[i] = left;
                xMin = Math.Min(xMin, left);
                yMin = Math.Min(yMin, Em(box.Y));
                xMax = Math.Max(xMax, right);
                yMax = Math.Max(yMax, Em(box.Y + box.Height));
                minRight = Math.Min(minRight, Advances[i] - right);
                MaxExtent = Math.Max(MaxExtent, right);
                MinLeft = Math.Min(MinLeft, left);
                Inked = true;
            }

            if (Inked)
            {
                (XMin, YMin, XMax, YMax, MinRight) = (xMin, yMin, xMax, yMax, minRight);
            }
            else
            {
                MinLeft = 0;
            }

            // A font without 'OS/2' has the line's alone.
            var typo = font.Ascender != 0 || font.Descender != 0;
            Ascender = Em(typo ? font.Ascender : font.LineAscent);
            Descender = Em(typo ? font.Descender : -font.LineDescent);
            LineGap = Em(font.LineGap);
        }

        public IFont Font { get; }

        public double Scale { get; }

        public ushort[] Advances { get; }

        public short[] Bearings { get; }

        public bool Inked { get; }

        public short XMin { get; }

        public short YMin { get; }

        public short XMax { get; }

        public short YMax { get; }

        public int MinRight { get; }

        public short MinLeft { get; } = short.MaxValue;

        public short MaxExtent { get; }

        public short Ascender { get; }

        public short Descender { get; }

        public short LineGap { get; }

        public short Em(double units) => (short)Math.Clamp(Math.Round(units * Scale), short.MinValue, short.MaxValue);
    }

    // The table directory, each table padded to four bytes, and the whole file's checksum made the magic number.
    private static byte[] File(SortedDictionary<string, byte[]> tables)
    {
        var count = tables.Count;
        var power = 1;
        var log = 0;
        while (power * 2 <= count)
        {
            power *= 2;
            log++;
        }

        var directory = 12 + 16 * count;
        var header = new byte[directory];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x4F54544F);   // 'OTTO': CFF outlines
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)(power * 16));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(8), (ushort)log);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), (ushort)(count * 16 - power * 16));
        var offset = directory;
        var record = 12;
        var headAt = 0;
        foreach (var (tag, data) in tables)
        {
            Encoding.ASCII.GetBytes(tag, header.AsSpan(record, 4));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 4), Checksum(data));
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 8), (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(record + 12), (uint)data.Length);
            if (tag == "head")
            {
                headAt = offset;
            }

            offset += Padded(data.Length);
            record += 16;
        }

        using var output = new MemoryStream();
        output.Write(header);
        foreach (var data in tables.Values)
        {
            output.Write(data);
            output.Write(new byte[Padded(data.Length) - data.Length]);
        }

        var file = output.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(headAt + 8), unchecked(ChecksumMagic - Checksum(file)));
        return file;
    }

    private static int Padded(int length) => (length + 3) & ~3;

    private static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (var at = 0; at < data.Length; at += 4)
        {
            uint word = 0;
            for (var b = 0; b < 4; b++)
            {
                word = (word << 8) | (at + b < data.Length ? data[at + b] : 0u);
            }

            sum = unchecked(sum + word);
        }

        return sum;
    }

    private static byte[] Head(Metrics metrics)
    {
        var data = new byte[54];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], 0x00010000);           // font revision
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], 0x5F0F3CF5);          // magic
        BinaryPrimitives.WriteUInt16BigEndian(span[16..], 0x000B);              // baseline and bearing at 0, whole ppem
        BinaryPrimitives.WriteUInt16BigEndian(span[18..], CffSubset.UnitsPerEm);
        BinaryPrimitives.WriteInt16BigEndian(span[36..], metrics.XMin);
        BinaryPrimitives.WriteInt16BigEndian(span[38..], metrics.YMin);
        BinaryPrimitives.WriteInt16BigEndian(span[40..], metrics.XMax);
        BinaryPrimitives.WriteInt16BigEndian(span[42..], metrics.YMax);
        BinaryPrimitives.WriteUInt16BigEndian(span[44..], (ushort)((IsBold(metrics.Font) ? 1 : 0) |
                                                                    (IsItalic(metrics.Font) ? 2 : 0)));
        BinaryPrimitives.WriteUInt16BigEndian(span[46..], (ushort)Math.Max(1, (int)metrics.Font.LowestRecPPEM));
        BinaryPrimitives.WriteInt16BigEndian(span[48..], 2);                    // left to right, with neutrals
        return data;
    }

    private static bool IsBold(IFont font) => font.Weight.Value >= 700;

    private static bool IsItalic(IFont font) => font.Style != FontStyle.Normal;

    private static byte[] Hhea(Metrics metrics)
    {
        var data = new byte[36];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(span[4..], metrics.Ascender);
        BinaryPrimitives.WriteInt16BigEndian(span[6..], metrics.Descender);
        BinaryPrimitives.WriteInt16BigEndian(span[8..], metrics.LineGap);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], metrics.Advances.Max());
        BinaryPrimitives.WriteInt16BigEndian(span[12..], metrics.MinLeft);
        BinaryPrimitives.WriteInt16BigEndian(span[14..], (short)Math.Clamp(metrics.MinRight, short.MinValue, short.MaxValue));
        BinaryPrimitives.WriteInt16BigEndian(span[16..], metrics.MaxExtent);
        BinaryPrimitives.WriteInt16BigEndian(span[18..], 1);                    // the caret upright
        BinaryPrimitives.WriteUInt16BigEndian(span[34..], (ushort)metrics.Advances.Length);
        return data;
    }

    private static byte[] Hmtx(Metrics metrics)
    {
        var data = new byte[metrics.Advances.Length * 4];
        for (var i = 0; i < metrics.Advances.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(i * 4), metrics.Advances[i]);
            BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(i * 4 + 2), metrics.Bearings[i]);
        }

        return data;
    }

    // Version 0.5, as a font of CFF outlines has it: the number of glyphs alone.
    private static byte[] Maxp(int glyphs)
    {
        var data = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(data, 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), (ushort)glyphs);
        return data;
    }

    private static byte[] Os2(Metrics metrics)
    {
        var font = metrics.Font;
        var data = new byte[96];
        var span = data.AsSpan();
        var inked = metrics.Advances.Where(advance => advance > 0).ToList();
        var em = CffSubset.UnitsPerEm;
        BinaryPrimitives.WriteUInt16BigEndian(span, 4);
        BinaryPrimitives.WriteInt16BigEndian(span[2..], (short)(inked.Count > 0 ? inked.Average(advance => advance) : 0));
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)Math.Clamp(font.Weight.Value, 1, 1000));
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], 5);                    // normal width
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], 0x0004);               // preview and print
        BinaryPrimitives.WriteInt16BigEndian(span[10..], (short)(em * 0.65));   // subscript size and offset
        BinaryPrimitives.WriteInt16BigEndian(span[12..], (short)(em * 0.6));
        BinaryPrimitives.WriteInt16BigEndian(span[16..], metrics.Em(font.SubscriptYOffset));
        BinaryPrimitives.WriteInt16BigEndian(span[18..], (short)(em * 0.65));   // superscript size and offset
        BinaryPrimitives.WriteInt16BigEndian(span[20..], (short)(em * 0.6));
        BinaryPrimitives.WriteInt16BigEndian(span[24..], metrics.Em(font.SuperscriptYOffset));
        BinaryPrimitives.WriteInt16BigEndian(span[26..], metrics.Em(font.StrikeoutSize));
        BinaryPrimitives.WriteInt16BigEndian(span[28..], metrics.Em(font.StrikeoutPosition));
        Encoding.ASCII.GetBytes("NONE", span.Slice(58, 4));                    // no vendor
        var selection = (IsItalic(font) ? 1 : 0) | (IsBold(font) ? 32 : 0);
        BinaryPrimitives.WriteUInt16BigEndian(span[62..], (ushort)(selection == 0 ? 64 : selection));
        BinaryPrimitives.WriteUInt16BigEndian(span[64..], 0x0020);
        BinaryPrimitives.WriteUInt16BigEndian(span[66..], 0xFFFF);
        BinaryPrimitives.WriteInt16BigEndian(span[68..], metrics.Ascender);
        BinaryPrimitives.WriteInt16BigEndian(span[70..], metrics.Descender);
        BinaryPrimitives.WriteInt16BigEndian(span[72..], metrics.LineGap);
        BinaryPrimitives.WriteUInt16BigEndian(span[74..], (ushort)Math.Max(0, (int)Math.Max(metrics.Ascender, metrics.YMax)));
        BinaryPrimitives.WriteUInt16BigEndian(span[76..], (ushort)Math.Max(0, -Math.Min(metrics.Descender, metrics.YMin)));
        BinaryPrimitives.WriteUInt32BigEndian(span[78..], 1);                   // Latin 1
        BinaryPrimitives.WriteInt16BigEndian(span[88..], metrics.Em(font.CapsHeight));
        BinaryPrimitives.WriteInt16BigEndian(span[86..], metrics.Em(font.CapsHeight * 0.7));   // no x-height said
        BinaryPrimitives.WriteUInt16BigEndian(span[92..], 0x0020);              // the break character
        return data;
    }

    // A run of characters standing for a run of glyphs, one after the other.
    private readonly record struct CharacterRun(int First, int Last, int Glyph);

    // The characters the glyphs written stand for alone, each to its glyph of the subset, on the Windows platform: all of
    // them in format 12, those of the Basic Multilingual Plane in format 4 as well - where they fit its 16-bit length.
    private static byte[] Cmap(IFont font, IReadOnlyList<uint> indices)
    {
        var characters = new SortedDictionary<int, int>();
        for (var i = 1; i < indices.Count; i++)
        {
            foreach (var text in font.GetGlyphText(indices[i]))
            {
                if (text.Length == 1 && !char.IsSurrogate(text[0]) ||
                    text.Length == 2 && char.IsSurrogatePair(text[0], text[1]))
                {
                    characters.TryAdd(char.ConvertToUtf32(text, 0), i);
                }
            }
        }

        var runs = Runs(characters);
        var basic = Runs(characters.Where(pair => pair.Key < 0xFFFF).ToDictionary(pair => pair.Key, pair => pair.Value));
        var format4 = Format4(16 + (basic.Count + 1) * 8 <= ushort.MaxValue ? basic : []);
        var format12 = Format12(runs);
        var data = new byte[4 + 2 * 8 + format4.Length + format12.Length];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 2);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], 3);                    // Windows, Unicode BMP
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], 1);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 20);
        BinaryPrimitives.WriteUInt16BigEndian(span[12..], 3);                   // Windows, Unicode full
        BinaryPrimitives.WriteUInt16BigEndian(span[14..], 10);
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], (uint)(20 + format4.Length));
        format4.CopyTo(span[20..]);
        format12.CopyTo(span[(20 + format4.Length)..]);
        return data;
    }

    private static List<CharacterRun> Runs(IEnumerable<KeyValuePair<int, int>> characters)
    {
        var runs = new List<CharacterRun>();
        foreach (var (character, glyph) in characters.OrderBy(pair => pair.Key))
        {
            if (runs.Count > 0 && runs[^1] is var last && last.Last + 1 == character &&
                last.Glyph + (last.Last - last.First) + 1 == glyph)
            {
                runs[^1] = last with { Last = character };
            }
            else
            {
                runs.Add(new CharacterRun(character, character, glyph));
            }
        }

        return runs;
    }

    // Format 4: a segment for each run, by its delta; then the last, 0xFFFF to glyph 0, as the format asks.
    private static byte[] Format4(IReadOnlyList<CharacterRun> runs)
    {
        var segments = runs.Count + 1;
        var power = 1;
        var log = 0;
        while (power * 2 <= segments)
        {
            power *= 2;
            log++;
        }

        var data = new byte[16 + segments * 8];
        var table = data.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(table, 4);
        BinaryPrimitives.WriteUInt16BigEndian(table[2..], (ushort)data.Length);
        BinaryPrimitives.WriteUInt16BigEndian(table[6..], (ushort)(segments * 2));
        BinaryPrimitives.WriteUInt16BigEndian(table[8..], (ushort)(power * 2));
        BinaryPrimitives.WriteUInt16BigEndian(table[10..], (ushort)log);
        BinaryPrimitives.WriteUInt16BigEndian(table[12..], (ushort)(segments * 2 - power * 2));
        var ends = table[14..];
        var starts = ends[(segments * 2 + 2)..];
        var deltas = starts[(segments * 2)..];
        for (var at = 0; at < runs.Count; at++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(ends[(at * 2)..], (ushort)runs[at].Last);
            BinaryPrimitives.WriteUInt16BigEndian(starts[(at * 2)..], (ushort)runs[at].First);
            BinaryPrimitives.WriteUInt16BigEndian(deltas[(at * 2)..], unchecked((ushort)(runs[at].Glyph - runs[at].First)));
        }

        BinaryPrimitives.WriteUInt16BigEndian(ends[(runs.Count * 2)..], 0xFFFF);
        BinaryPrimitives.WriteUInt16BigEndian(starts[(runs.Count * 2)..], 0xFFFF);
        BinaryPrimitives.WriteUInt16BigEndian(deltas[(runs.Count * 2)..], 1);
        return data;
    }

    // Format 12: a group for each run.
    private static byte[] Format12(IReadOnlyList<CharacterRun> runs)
    {
        var data = new byte[16 + runs.Count * 12];
        var table = data.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(table, 12);
        BinaryPrimitives.WriteUInt32BigEndian(table[4..], (uint)data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(table[12..], (uint)runs.Count);
        for (var at = 0; at < runs.Count; at++)
        {
            var group = table[(16 + at * 12)..];
            BinaryPrimitives.WriteUInt32BigEndian(group, (uint)runs[at].First);
            BinaryPrimitives.WriteUInt32BigEndian(group[4..], (uint)runs[at].Last);
            BinaryPrimitives.WriteUInt32BigEndian(group[8..], (uint)runs[at].Glyph);
        }

        return data;
    }

    // The family, the face and the PostScript name - the CFF program's - in UTF-16 on the Windows platform.
    // The face as 'head' and 'OS/2' have it: bold, italic, both, or regular.
    private static string Face(IFont font) => (IsBold(font), IsItalic(font)) switch
    {
        (true, true) => "Bold Italic",
        (true, false) => "Bold",
        (false, true) => "Italic",
        _ => "Regular",
    };

    private static byte[] Name(string postScript, string face)
    {
        var names = new (ushort Id, string Text)[]
        {
            (1, postScript), (2, face), (3, postScript), (4, postScript), (5, "Version 1.0"), (6, postScript),
        };
        var strings = names.Select(name => Encoding.BigEndianUnicode.GetBytes(name.Text)).ToList();
        var storage = 6 + names.Length * 12;
        var data = new byte[storage + strings.Sum(text => text.Length)];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)names.Length);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)storage);
        var offset = 0;
        for (var i = 0; i < names.Length; i++)
        {
            var record = span[(6 + i * 12)..];
            BinaryPrimitives.WriteUInt16BigEndian(record, 3);
            BinaryPrimitives.WriteUInt16BigEndian(record[2..], 1);
            BinaryPrimitives.WriteUInt16BigEndian(record[4..], 0x0409);
            BinaryPrimitives.WriteUInt16BigEndian(record[6..], names[i].Id);
            BinaryPrimitives.WriteUInt16BigEndian(record[8..], (ushort)strings[i].Length);
            BinaryPrimitives.WriteUInt16BigEndian(record[10..], (ushort)offset);
            strings[i].CopyTo(span[(storage + offset)..]);
            offset += strings[i].Length;
        }

        return data;
    }

    // Version 3: no glyph names, as a font of CFF outlines keeps them in its program.
    private static byte[] Post(Metrics metrics)
    {
        var data = new byte[32];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00030000);
        BinaryPrimitives.WriteInt32BigEndian(span[4..], IsItalic(metrics.Font) ? -12 << 16 : 0);
        BinaryPrimitives.WriteInt16BigEndian(span[8..], metrics.Em(metrics.Font.UnderlinePosition));
        BinaryPrimitives.WriteInt16BigEndian(span[10..], metrics.Em(metrics.Font.UnderlineThickness));
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], metrics.Font.IsMonospaced ? 1u : 0u);
        return data;
    }
}
