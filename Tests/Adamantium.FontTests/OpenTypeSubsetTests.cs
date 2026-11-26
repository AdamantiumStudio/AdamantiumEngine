using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Subsetting;
using NUnit.Framework;

namespace Adamantium.FontTests;

// A subset written as an OpenType file and read back as one: its glyphs at 1000 units to the em, their widths, their
// boxes and the characters they stand for, as the font they came from has them.
public class OpenTypeSubsetTests
{
    private static readonly string[] Files =
        Directory.Exists("TTFFonts")
            ? Directory.GetFiles("TTFFonts", "*.ttf", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("OTFFonts", "*.otf", SearchOption.AllDirectories)).OrderBy(file => file).ToArray()
            : [];

    [Test]
    public void EachFont_IsReadBackAsAnOpenTypeFont_WithItsWidthsAndCharacters()
    {
        Assume.That(Files, Is.Not.Empty);
        var refused = new List<string>();
        var checkedFonts = 0;
        foreach (var file in Files)
        {
            IFont font;
            try
            {
                font = Typeface.LoadFont(file, 3).GetFont(0);
            }
            catch (System.Exception)
            {
                refused.Add(file);   // a file kept to be refused
                continue;
            }

            if (!font.TryGetGlyphIndex('A', out var a))
            {
                continue;
            }

            var asked = new List<uint> { a };
            asked.AddRange(Enumerable.Range(0, (int)font.Typeface.GlyphCount).Where(index => index % 17 == 3)
                .Select(index => (uint)index));
            var written = CffSubset.IndicesOf(font, asked);
            var read = Typeface.LoadFont(OpenTypeSubset.Write(font, written, "ABCDEF+Probe"), 3).GetFont(0);
            var scale = (double)CffSubset.UnitsPerEm / font.UnitsPerEm;

            Assert.That(read.Typeface.GlyphCount, Is.EqualTo(written.Count), file);
            Assert.That(read.UnitsPerEm, Is.EqualTo(CffSubset.UnitsPerEm), file);
            for (var at = 0; at < written.Count; at++)
            {
                Assert.That(read.GetAdvanceWidth((uint)at),
                    Is.EqualTo(font.GetAdvanceWidth(written[at]) * scale).Within(0.51), $"{file}, glyph {written[at]}");
                // The outlines themselves are the CFF program's, read back point for point in CffSubsetTests.
                Assert.That(read.GetGlyphByIndex((uint)at).HasOutlines,
                    Is.EqualTo(font.GetGlyphByIndex(written[at]).HasOutlines), $"{file}, glyph {written[at]}: outline");
            }

            Assert.That(read.TryGetGlyphIndex('A', out var readA) && readA == written.ToList().IndexOf(a), Is.True,
                $"{file}: 'A' does not stand for its glyph");
            checkedFonts++;
        }

        Assert.That(checkedFonts, Is.GreaterThan(0));
        Assert.That(refused.Count, Is.LessThan(Files.Length / 10), string.Join(", ", refused));
    }

    // Asked for out of order, without glyph 0, twice: laid out as the CFF program lays them, every table alike.
    [Test]
    public void GlyphsAskedForInAnyOrder_AreLaidOutAsTheProgramHasThem()
    {
        var font = Typeface.LoadFont(Path.Combine("TTFFonts", "SourceSans3-Regular.ttf"), 3).GetFont(0);
        font.TryGetGlyphIndex('B', out var b);
        font.TryGetGlyphIndex('A', out var a);
        var scale = (double)CffSubset.UnitsPerEm / font.UnitsPerEm;

        var read = Typeface.LoadFont(OpenTypeSubset.Write(font, [b, a, b], "ABCDEF+Probe"), 3).GetFont(0);

        var first = System.Math.Min(a, b);
        Assert.Multiple(() =>
        {
            Assert.That(read.Typeface.GlyphCount, Is.EqualTo(3), "glyph 0, then the two, once each");
            Assert.That(read.GetAdvanceWidth(1), Is.EqualTo(font.GetAdvanceWidth(first) * scale).Within(0.51),
                "the second glyph's width is not the lower index's");
            Assert.That(read.TryGetGlyphIndex('A', out var readA) && readA == (a < b ? 1u : 2u), Is.True);
        });
    }
}
