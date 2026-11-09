using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using NUnit.Framework;
using static Adamantium.FontTests.SfntBuilder;

namespace Adamantium.FontTests;

/// <summary>TrueType programs as PDF embeds them (FontFile2): subsets that keep only the tables a renderer needs.</summary>
public class TrimmedTrueTypeTests
{
    private const string FontPath = "ScriptFonts/NotoSansAdlam-Regular.ttf";

    private static Typeface Whole() => Typeface.LoadFont(FontPath, 3);

    private static Typeface Load(byte[] data) => Typeface.LoadTrueTypeProgram(data);

    private static void AssertSameOutlines(Typeface trimmed, Typeface whole)
    {
        Assert.That(trimmed.GlyphCount, Is.EqualTo(whole.GlyphCount));
        for (var index = 0u; index < whole.GlyphCount; index += 3)
        {
            trimmed.GetGlyphByIndex(index, out var mine);
            whole.GetGlyphByIndex(index, out var theirs);
            Assert.That(mine.IsInvalid, Is.EqualTo(theirs.IsInvalid), $"glyph {index}");
            Assert.That(mine.BoundingRectangle, Is.EqualTo(theirs.BoundingRectangle), $"glyph {index}");
        }
    }

    [TestCase("head", "maxp", "loca", "glyf", "hhea", "hmtx")]
    [TestCase("head", "maxp", "loca", "glyf", "hhea", "hmtx", "cvt ", "fpgm", "prep")]
    [TestCase("head", "maxp", "loca", "glyf", "hhea", "hmtx", "cmap")]
    [TestCase("head", "maxp", "loca", "glyf")]
    [TestCase("head", "loca", "glyf", "hhea", "hmtx")]
    [TestCase("head", "maxp", "loca", "glyf", "hmtx")]
    public void SubsetWithOnlySomeTablesDrawsTheSameGlyphs(params string[] kept)
    {
        var tables = TablesOf(File.ReadAllBytes(FontPath));
        var whole = Whole();

        var trimmed = Load(Sfnt(tables.Where(t => kept.Contains(t.Key)).ToDictionary(t => t.Key, t => t.Value)));

        AssertSameOutlines(trimmed, whole);
        if (kept.Contains("hmtx"))
        {
            Assert.That(trimmed.Glyphs.Select(g => g.AdvanceWidth), Is.EqualTo(whole.Glyphs.Select(g => g.AdvanceWidth)));
        }
    }

    [Test]
    public void FullFontLoadStillWantsTheMandatoryTables()
    {
        var tables = TablesOf(File.ReadAllBytes(FontPath));
        tables.Remove("cmap");

        Assert.That(() => Typeface.LoadFont(Sfnt(tables), 3), Throws.Exception);
    }

    [Test]
    public void ProgramThatIsNotTrueTypeIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Typeface.LoadTrueTypeProgram([]));
        Assert.Throws<InvalidDataException>(() => Typeface.LoadTrueTypeProgram("OTTO"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => Typeface.LoadTrueTypeProgram([0, 1, 0, 0, 0, 1]));
    }

    [Test]
    public void TrueTypeTagReadsAsTheVersionNumberDoes()
    {
        var data = File.ReadAllBytes(FontPath);
        var tables = TablesOf(data);

        var trimmed = Load(Sfnt(tables, 0x74727565));

        AssertSameOutlines(trimmed, Whole());
    }

    [Test]
    public void LocaPointingPastGlyfLeavesThoseGlyphsInvalid()
    {
        var data = File.ReadAllBytes(FontPath);
        var tables = TablesOf(data);
        tables["glyf"] = tables["glyf"][..(tables["glyf"].Length / 2)];

        var trimmed = Load(Sfnt(tables));

        Assert.That(trimmed.GlyphCount, Is.GreaterThan(0));
        foreach (var glyph in trimmed.Glyphs)
        {
            _ = glyph.BoundingRectangle;
        }

        Assert.That(trimmed.Glyphs.Any(g => g.IsInvalid));
        Assert.That(trimmed.Glyphs.Any(g => !g.IsInvalid && g.HasOutlines));
    }

    [Test]
    public void LayoutTablesAndDamagedOptionalTablesDoNotStopAProgram()
    {
        var tables = TablesOf(File.ReadAllBytes(FontPath));
        var garbage = Enumerable.Repeat((byte)0xFF, 64).ToArray();
        tables["GSUB"] = garbage;
        tables["GPOS"] = garbage;
        tables["GDEF"] = garbage;
        tables["cmap"] = garbage;
        tables["post"] = garbage;

        var trimmed = Load(Sfnt(tables));

        AssertSameOutlines(trimmed, Whole());
        Assert.That(trimmed.CurrentFont, Is.Not.Null);
    }

    [Test]
    public void CompositesThatDoubleAtEachLevelStopAtTheirPointLimit()
    {
        var glyphs = new List<byte[]> { SimpleGlyph(3) };
        for (var level = 1; level <= 30; level++)
        {
            glyphs.Add(CompositeGlyph(level - 1, level - 1));
        }

        var typeface = Load(TrueType(glyphs));
        var watch = Stopwatch.StartNew();

        typeface.GetGlyphByIndex(30, out var deepest);
        typeface.GetGlyphByIndex(15, out var tooMany);
        typeface.GetGlyphByIndex(14, out var mostAllowed);

        Assert.That(deepest.Outlines.Sum(o => o.Points.Count), Is.Zero, "built of glyphs left without points");
        Assert.That(tooMany.IsInvalid, Is.True, "3 << 15 points do not fit 16 bits");
        Assert.That(mostAllowed.IsInvalid, Is.False);
        Assert.That(mostAllowed.Outlines.Sum(o => o.Points.Count), Is.EqualTo(3 << 14));
        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(5));
    }

    [Test]
    public void ManyGlyphsBuiltOfOneLargeCompositeStopAtTheFontsPointBudget()
    {
        var glyphs = new List<byte[]> { SimpleGlyph(3) };
        for (var level = 1; level <= 14; level++)
        {
            glyphs.Add(CompositeGlyph(level - 1, level - 1));
        }

        for (var i = 0; i < 300; i++)
        {
            glyphs.Add(CompositeGlyph(14));
        }

        var data = TrueType(glyphs);
        var typeface = Load(data);
        var watch = Stopwatch.StartNew();
        var drawn = 0L;
        foreach (var glyph in typeface.Glyphs)
        {
            drawn += glyph.Outlines.Sum(o => o.Points.Count);
        }

        Assert.That(drawn, Is.LessThan(1_000_000 + 8L * data.Length), "points copied stay in proportion to the font");
        Assert.That(typeface.Glyphs.Last().IsInvalid, Is.True);
        Assert.That(watch.Elapsed.TotalSeconds, Is.LessThan(5));
    }

    [TestCase(12, TestName = "cmap format 12 claiming 4 billion groups")]
    [TestCase(13, TestName = "cmap format 13 claiming 4 billion groups")]
    [TestCase(14, TestName = "cmap format 14 claiming 4 billion selectors")]
    [TestCase(4, TestName = "cmap format 4 of length 0")]
    [TestCase(2, TestName = "cmap format 2")]
    [TestCase(8, TestName = "cmap format 8")]
    [TestCase(10, TestName = "cmap format 10")]
    [TestCase(99, TestName = "cmap format 99")]
    public void HostileOrUnreadCmapSubtableLeavesTheGlyphs(int format)
    {
        var subtable = new byte[32];
        BinaryPrimitives.WriteUInt16BigEndian(subtable, (ushort)format);
        switch (format)
        {
            case 12 or 13:
                BinaryPrimitives.WriteUInt32BigEndian(subtable.AsSpan(12), uint.MaxValue);
                break;
            case 14:
                BinaryPrimitives.WriteUInt32BigEndian(subtable.AsSpan(6), uint.MaxValue);
                break;
            case 4:
                BinaryPrimitives.WriteUInt16BigEndian(subtable.AsSpan(6), 2);
                break;
        }

        var cmap = new byte[12 + subtable.Length];
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(cmap.AsSpan(6), 10);
        BinaryPrimitives.WriteUInt32BigEndian(cmap.AsSpan(8), 12);
        subtable.CopyTo(cmap, 12);
        var tables = TablesOf(File.ReadAllBytes(FontPath));
        tables["cmap"] = cmap;

        var trimmed = Load(Sfnt(tables));

        AssertSameOutlines(trimmed, Whole());
    }

    [Test]
    public void CompositeThatContainsItselfIsDrawnWithoutIt()
    {
        var typeface = Load(TrueType([SimpleGlyph(3), CompositeGlyph(0, 1)]));

        typeface.GetGlyphByIndex(1, out var composite);

        Assert.That(composite.Outlines.Sum(o => o.Points.Count), Is.EqualTo(3));
    }

    [Test]
    public void EveryByteChangedGivesAFontOrInvalidData()
    {
        var data = TrueType([SimpleGlyph(3), CompositeGlyph(0, 0), SimpleGlyph(1)]);
        foreach (var value in new byte[] { 0, 1, 0x7F, 0xFF })
        {
            for (var at = 0; at < data.Length; at++)
            {
                var changed = (byte[])data.Clone();
                changed[at] = value;
                try
                {
                    foreach (var glyph in Load(changed).Glyphs)
                    {
                        _ = glyph.BoundingRectangle;
                    }
                }
                catch (InvalidDataException)
                {
                }
            }
        }
    }

    [Test]
    public void CutTablesAreRejectedOrRead()
    {
        var data = File.ReadAllBytes(FontPath);
        var tables = TablesOf(data);
        foreach (var tag in new[] { "head", "maxp", "loca", "hhea", "hmtx", "cmap", "name", "OS/2", "post" })
        {
            if (!tables.TryGetValue(tag, out var table))
            {
                continue;
            }

            foreach (var length in new[] { 0, 1, 3, table.Length / 2 })
            {
                var cut = new Dictionary<string, byte[]>(tables) { [tag] = table[..length] };
                try
                {
                    var typeface = Load(Sfnt(cut));
                    foreach (var glyph in typeface.Glyphs.Take(20))
                    {
                        _ = glyph.BoundingRectangle;
                    }
                }
                catch (InvalidDataException)
                {
                }
            }
        }
    }
}
