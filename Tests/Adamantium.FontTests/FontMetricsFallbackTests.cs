using System.IO;
using System.Text;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class FontMetricsFallbackTests
{
    private const string Cascadia = "TTFFonts/CascadiaCode-Regular.ttf";
    private const string SourceSans = "source-sans-3v028R/VAR/SourceSans3VF-Roman.ttf";

    [Test]
    public void AnOs2TableBeforeVersion2_TakesTheCapitalsFromTheH_AndKeepsItsLineMetrics()
    {
        var bytes = File.ReadAllBytes(Cascadia);
        var current = Typeface.LoadFont(bytes, 3).GetFont(0);
        var old = Typeface.LoadFont(WithOs2Version(bytes, 1), 3).GetFont(0);
        var h = old.GetGlyphByUnicode('H').BoundingRectangle;

        Assert.Multiple(() =>
        {
            Assert.That(old.CapsHeight, Is.EqualTo((short)(h.Y + h.Height)));
            Assert.That(old.Ascender, Is.EqualTo(current.Ascender));
            Assert.That(old.Descender, Is.EqualTo(current.Descender));
            Assert.That(old.LineAscent + old.LineDescent, Is.EqualTo(current.LineAscent + current.LineDescent));
        });
    }

    [Test]
    public void AnInstancesCapHeight_IsItsOwnH_WhateverWasReadBefore()
    {
        var bytes = WithOs2Version(File.ReadAllBytes(SourceSans), 1);
        var baseRead = Typeface.LoadFont(bytes, 3).GetFont(0);
        _ = baseRead.CapsHeight;
        var afterBase = baseRead.GetInstance([new FontVariation("wght", 900)]);
        var alone = Typeface.LoadFont(bytes, 3).GetFont(0).GetInstance([new FontVariation("wght", 900)]);
        var h = alone.GetGlyphByUnicode('H').BoundingRectangle;

        Assert.That(afterBase.CapsHeight, Is.EqualTo(alone.CapsHeight));
        Assert.That(alone.CapsHeight, Is.EqualTo((short)(h.Y + h.Height)));
    }

    [Test]
    public void AVariableInstanceAtANamedStyle_IsCalledByIt()
    {
        var font = Typeface.LoadFont(SourceSans, 3).GetFont(0);
        var bold = font.GetInstance([new FontVariation("wght", 700)]);
        var between = font.GetInstance([new FontVariation("wght", 650)]);

        Assert.That(bold.FullName, Does.EndWith("Bold"));
        Assert.That(between.FullName, Is.EqualTo(font.FullName));
    }

    private static byte[] WithOs2Version(byte[] bytes, ushort version)
    {
        var copy = (byte[])bytes.Clone();
        var tables = (copy[4] << 8) | copy[5];
        for (var i = 0; i < tables; i++)
        {
            var record = 12 + 16 * i;
            if (Encoding.ASCII.GetString(copy, record, 4) == "OS/2")
            {
                var offset = (copy[record + 8] << 24) | (copy[record + 9] << 16) | (copy[record + 10] << 8) | copy[record + 11];
                copy[offset] = (byte)(version >> 8);
                copy[offset + 1] = (byte)version;
                return copy;
            }
        }

        Assert.Fail("the font has no OS/2 table");
        return copy;
    }
}
