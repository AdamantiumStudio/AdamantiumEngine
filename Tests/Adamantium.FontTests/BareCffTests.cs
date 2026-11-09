using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using NUnit.Framework;

namespace Adamantium.FontTests;

public class BareCffTests
{
    /// <summary>The <c>CFF </c> table of an OpenType file, which is the font program PDF embeds as FontFile3.</summary>
    private static byte[] CffTableOf(string path)
    {
        var data = File.ReadAllBytes(path);
        var tables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        for (var i = 0; i < tables; i++)
        {
            var record = data.AsSpan(12 + i * 16);
            if (record[..4].SequenceEqual("CFF "u8))
            {
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
                return data.AsSpan(offset, length).ToArray();
            }
        }

        throw new InvalidOperationException("No CFF table in " + path);
    }

    [TestCase("OTFFonts/CFF/Quicksand-Regular.otf")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf")]
    [TestCase("OTFFonts/Crimson-Italic.otf")]
    public void BareCffDrawsTheGlyphsOfItsOpenTypeFile(string path)
    {
        var openType = Typeface.LoadFont(path, 3);
        var bare = Typeface.LoadCff(CffTableOf(path));

        Assert.That(bare.GlyphCount, Is.EqualTo(openType.GlyphCount));
        Assert.That(bare.CurrentFont.UnitsPerEm, Is.EqualTo(openType.GetFont(0).UnitsPerEm));
        Assert.That(bare.IsCidKeyed, Is.False);
        for (var index = 0u; index < bare.GlyphCount; index += 7)
        {
            bare.GetGlyphByIndex(index, out var mine);
            openType.GetGlyphByIndex(index, out var theirs);
            Assert.That(mine.Name, Is.EqualTo(theirs.Name), $"glyph {index}");
            Assert.That(mine.BoundingRectangle, Is.EqualTo(theirs.BoundingRectangle), $"glyph {index}");
        }
    }

    [Test]
    public void GlyphsAreFoundByNameAndByTheBuiltInEncoding()
    {
        var bare = Typeface.LoadCff(CffTableOf("OTFFonts/SourceSans3-Regular.otf"));

        Assert.That(bare.GetGlyphByName("A", out var a), Is.True);
        Assert.That(bare.GetGlyphByName("no-such-glyph", out _), Is.False);
        Assert.That(bare.BuiltInEncoding, Has.Count.EqualTo(256));
        Assert.That(bare.BuiltInEncoding[65], Is.EqualTo((int)a.Index), "StandardEncoding unless the font says otherwise");
    }

    [Test]
    public void CidKeyedFontFindsGlyphsByCid()
    {
        var path = "OTFFonts/FDArrayTest257.otf";
        var openType = Typeface.LoadFont(path, 3);
        var bare = Typeface.LoadCff(CffTableOf(path));

        Assert.That(bare.IsCidKeyed, Is.True);
        Assert.That(bare.GetGlyphByIndex(1, out var first) && first.Name == null, "CID fonts name no glyphs past .notdef");
        var checkedGlyphs = 0;
        for (var index = 1u; index < bare.GlyphCount; index += 13)
        {
            bare.GetGlyphByIndex(index, out var glyph);
            Assert.That(bare.GetGlyphByCid(glyph.SID, out var byCid) && ReferenceEquals(byCid, glyph), $"glyph {index}");
            openType.GetGlyphByIndex(index, out var theirs);
            Assert.That(glyph.BoundingRectangle, Is.EqualTo(theirs.BoundingRectangle), $"glyph {index}");
            checkedGlyphs++;
        }

        Assert.That(checkedGlyphs, Is.GreaterThan(5));
        Assert.That(bare.GetGlyphByCid(0, out var zero) && zero.Index == 0);
    }

    [Test]
    public void DataThatIsNotCffIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => Typeface.LoadCff([]));
        Assert.Throws<InvalidDataException>(() => Typeface.LoadCff([2, 0, 4, 4, 0, 0]));
    }

    [Test]
    public void TruncatedCffIsRejectedOrReadsWhatItHolds()
    {
        var data = CffTableOf("OTFFonts/CFF/Quicksand-Regular.otf");
        foreach (var length in new[] { 4, 10, 40, 200, 1000, data.Length / 3, data.Length / 2, data.Length - 10 })
        {
            try
            {
                var typeface = Typeface.LoadCff(data[..length]);
                foreach (var glyph in typeface.Glyphs.Take(50))
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
