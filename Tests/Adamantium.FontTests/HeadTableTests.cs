using System;
using System.Buffers.Binary;
using System.IO;
using Adamantium.Core.Shared.Fonts;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class HeadTableTests
{
    private const string FontPath = "ScriptFonts/NotoSansAdlam-Regular.ttf";

    /// <summary>The font with its head dates set, in seconds since 1904.</summary>
    private static byte[] WithDates(long created, long modified)
    {
        var tables = SfntBuilder.TablesOf(File.ReadAllBytes(FontPath));
        BinaryPrimitives.WriteInt64BigEndian(tables["head"].AsSpan(20), created);
        BinaryPrimitives.WriteInt64BigEndian(tables["head"].AsSpan(28), modified);
        return SfntBuilder.Sfnt(tables);
    }

    [Test]
    public void DatesAreSecondsSince1904()
    {
        var font = Typeface.LoadFont(WithDates(3_000_000_000, 3_600_000_000), 3).GetFont(0);

        Assert.That(font.Created, Is.EqualTo(new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(3_000_000_000)));
        Assert.That(font.Modified.Year, Is.EqualTo(2018));
    }

    // Apple's own fonts may have no OS/2 table (Courier.ttc on macOS): such a font loads, bold and italic by its head.
    [Test]
    public void AFontWithoutOs2_Loads_AndIsBoldAndItalicByItsHeadsMacStyle()
    {
        var tables = SfntBuilder.TablesOf(File.ReadAllBytes(FontPath));
        tables.Remove("OS/2");
        BinaryPrimitives.WriteUInt16BigEndian(tables["head"].AsSpan(44), 0b11);

        var font = Typeface.LoadFont(SfntBuilder.Sfnt(tables), 3).GetFont(0);

        Assert.Multiple(() =>
        {
            Assert.That(font.Weight, Is.EqualTo(FontWeight.Bold));
            Assert.That(font.Style, Is.EqualTo(FontStyle.Italic));
            Assert.That(font.LineAscent + font.LineDescent, Is.GreaterThan(0), "line metrics from hhea");
        });
    }

    [Test]
    public void DateOutOfRangeFallsBackToTheEpoch()
    {
        var font = Typeface.LoadFont(WithDates(long.MaxValue, -5), 3).GetFont(0);

        Assert.That(font.Created, Is.EqualTo(new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(font.Modified, Is.EqualTo(font.Created));
    }
}
