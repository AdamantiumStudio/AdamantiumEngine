using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Adamantium.Fonts;
using Adamantium.Fonts.Common;
using Adamantium.Fonts.Extensions;
using Adamantium.Fonts.Tables.CMAP;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>cmap subtables of formats 2, 8 and 10, laid out byte by byte as the OpenType specification gives them;
/// each reader starts after the format field. In the format 2 sample the two subheaders start the 16 bytes before the
/// glyph array, and an idRangeOffset counts from its own field, 6 bytes into its subheader.</summary>
public class CharacterMapFormatTests
{
    /// <summary>One-byte codes 0x41 to 0x43 to glyphs 10 to 12; lead byte 0x81 with second bytes 0x40 and 0x41 to
    /// glyphs 20 and 21, plus an idDelta of 5.</summary>
    private static byte[] Format2()
    {
        var body = new List<byte>();
        void U16(int value) => body.AddRange([(byte)(value >> 8), (byte)value]);

        U16(6 + 512 + 16 + 10);
        U16(0);
        for (var first = 0; first < 256; first++)
        {
            U16(first == 0x81 ? 8 : 0);
        }

        U16(0x41); U16(3); U16(0); U16(16 - 6);
        U16(0x40); U16(2); U16(5); U16(16 + 6 - 14);
        foreach (var glyph in new[] { 10, 11, 12, 20, 21 })
        {
            U16(glyph);
        }

        return body.ToArray();
    }

    [Test]
    public void Format2MapsOneAndTwoByteCodes()
    {
        var map = new FontStreamReader(Format2()).ReadCharacterMapFormat2();

        Assert.That(map.GetGlyphIndex(0x41), Is.EqualTo(10));
        Assert.That(map.GetGlyphIndex(0x43), Is.EqualTo(12));
        Assert.That(map.GetGlyphIndex(0x44), Is.Zero, "past the one-byte range");
        Assert.That(map.GetGlyphIndex(0x81), Is.Zero, "a lead byte alone is no character");
        Assert.That(map.GetGlyphIndex(0x8140), Is.EqualTo(25), "20 plus idDelta 5");
        Assert.That(map.GetGlyphIndex(0x8141), Is.EqualTo(26));
        Assert.That(map.GetGlyphIndex(0x8142), Is.Zero);
        Assert.That(map.GetGlyphIndex(0x8240), Is.Zero, "0x82 is a one-byte code, not a lead byte");

        var mapping = new Dictionary<uint, uint>();
        map.GetUnicodeToGlyphMappings(mapping);
        Assert.That(mapping, Is.EquivalentTo(new Dictionary<uint, uint>
        {
            [0x41] = 10, [0x42] = 11, [0x43] = 12, [0x8140] = 25, [0x8141] = 26,
        }));
    }

    [Test]
    public void Format2ShorterThanItsSubheadersIsRejected()
    {
        var data = Format2();
        BinaryPrimitives.WriteUInt16BigEndian(data, 100);

        Assert.Throws<InvalidDataException>(() => new FontStreamReader(data).ReadCharacterMapFormat2());
    }

    private static byte[] Format8()
    {
        var data = new byte[2 + 4 + 4 + 8192 + 4 + 2 * 12];
        var at = 10;
        data[at + 0xD800 / 8] = 0x80;
        at += 8192;
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), 2);
        at += 4;
        foreach (var (start, end, glyph) in new[] { (0x10000u, 0x10002u, 7u), (0x41u, 0x42u, 3u) })
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), start);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at + 4), end);
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at + 8), glyph);
            at += 12;
        }

        return data;
    }

    [Test]
    public void Format8MapsGroupsGivenInAnyOrder()
    {
        var map = new FontStreamReader(Format8()).ReadCharacterMapFormat8();

        Assert.That(map.GetGlyphIndex(0x41), Is.EqualTo(3));
        Assert.That(map.GetGlyphIndex(0x42), Is.EqualTo(4));
        Assert.That(map.GetGlyphIndex(0x10001), Is.EqualTo(8));
        Assert.That(map.GetGlyphIndex(0x43), Is.Zero);
        Assert.That(map.StartsLongCode(0xD800), Is.True);
        Assert.That(map.StartsLongCode(0xD801), Is.False);

        var codes = new List<uint>();
        map.CollectUnicodeChars(codes);
        Assert.That(codes, Is.EqualTo(new uint[] { 0x41, 0x42, 0x10000, 0x10001, 0x10002 }));
    }

    private static byte[] Format10(uint start, uint count, params ushort[] glyphs)
    {
        var data = new byte[2 + 4 * 4 + 2 * glyphs.Length];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(10), start);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(14), count);
        for (var i = 0; i < glyphs.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(18 + 2 * i), glyphs[i]);
        }

        return data;
    }

    [Test]
    public void Format10MapsItsRun()
    {
        var map = new FontStreamReader(Format10(0x1F600, 3, 4, 5, 6)).ReadCharacterMapFormat10();

        Assert.That(map.GetGlyphIndex(0x1F600), Is.EqualTo(4));
        Assert.That(map.GetGlyphIndex(0x1F602), Is.EqualTo(6));
        Assert.That(map.GetGlyphIndex(0x1F603), Is.Zero);
        Assert.That(map.GetGlyphIndex(0x1F5FF), Is.Zero);

        var mapping = new Dictionary<uint, uint>();
        map.GetUnicodeToGlyphMappings(mapping);
        Assert.That(mapping, Has.Count.EqualTo(3));
    }

    [Test]
    public void Format10ClaimingMoreCharactersThanItHoldsIsRejected()
    {
        Assert.Throws<EndOfStreamException>(() =>
            new FontStreamReader(Format10(0x1F600, uint.MaxValue, 4)).ReadCharacterMapFormat10());
    }

    [Test]
    public void FontWhoseOnlyCmapIsFormat10FindsItsCharacters()
    {
        var subtable = new byte[2 + 18 + 2 * 3];
        BinaryPrimitives.WriteUInt16BigEndian(subtable, 10);
        Format10(0x1F600, 3, 1, 2, 1).AsSpan().CopyTo(subtable.AsSpan(2));
        var cmap = new byte[12 + subtable.Length];
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 10);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);
        subtable.CopyTo(cmap, 12);
        var tables = SfntBuilder.TablesOf(SfntBuilder.TrueType(
            [SfntBuilder.SimpleGlyph(3), SfntBuilder.SimpleGlyph(4), SfntBuilder.SimpleGlyph(5)]));
        tables["cmap"] = cmap;

        var font = Typeface.LoadTrueTypeProgram(SfntBuilder.Sfnt(tables)).GetFont(0);

        Assert.That(font.GetGlyphByUnicode(0x1F601).Index, Is.EqualTo(2));
        Assert.That(font.GetGlyphByUnicode(0x1F602).Index, Is.EqualTo(1));
    }
}
