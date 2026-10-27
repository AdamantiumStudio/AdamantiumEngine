using System;
using System.IO;
using System.Linq;
using Adamantium.Core.Fonts.Shaping;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>Shaping a range of a string: the range shapes as if alone, its letters joining with the text around it.</summary>
public class TextShaperRangeTests
{
    private static IFont Font(string path) =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, path), 3).GetFont(0);

    private static string Glyphs(ShapedGlyph[] glyphs) => string.Join(" ", glyphs.Select(g => g.ToString()));

    [Test]
    public void ARange_ShapesAsTheTextAlone_ItsFeatureRangesCountingFromItsStart()
    {
        var font = Font("TTFFonts/SourceSans3-Regular.ttf");
        var options = new ShapingOptions("Latn", null, FontFeature.ParseList("liga[0:3]=0"));

        var range = TextShaper.Shape(font, "xxoffice", 2, 8, options);

        Assert.That(Glyphs(range), Is.EqualTo(Glyphs(TextShaper.Shape(font, "office", options))));
    }

    // The middle letter of a word, shaped alone in its range, takes the form it has between its neighbours.
    [Test]
    public void ALetterInARange_JoinsWithTheLettersAroundIt()
    {
        var font = Font("ScriptFonts/NotoSansArabic-Regular.ttf");
        var word = TextShaper.Shape(font, "محمد");

        var middle = TextShaper.Shape(font, "محمد", 1, 2);

        Assert.Multiple(() =>
        {
            Assert.That(middle.Single().GlyphIndex, Is.EqualTo(word.Single(g => g.Cluster == 1).GlyphIndex));
            Assert.That(middle.Single().GlyphIndex, Is.Not.EqualTo(TextShaper.Shape(font, "ح").Single().GlyphIndex));
            Assert.That(middle.Single().Cluster, Is.EqualTo(0));
        });
    }

    [TestCase(-1, 2)]
    [TestCase(0, 9)]
    [TestCase(3, 2)]
    public void ARangeOutsideTheText_IsRefused(int start, int end)
    {
        var font = Font("TTFFonts/SourceSans3-Regular.ttf");

        Assert.Throws<ArgumentOutOfRangeException>(() => TextShaper.Shape(font, "office", start, end));
    }
}
