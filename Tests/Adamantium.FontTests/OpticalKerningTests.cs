using System;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using NUnit.Framework;

namespace Adamantium.FontTests;

// Kerning measured from the outlines: pairs a type designer kerns move closer, pairs of straight stems barely move, and
// whatever moves never brings two glyphs' ink to touch.
public class OpticalKerningTests
{
    private static IFont Font(string path) => Typeface.LoadFont(path, 3).GetFont(0);

    private static int Kern(IFont font, string pair)
    {
        font.TryGetGlyphIndex(pair[0], out var left);
        font.TryGetGlyphIndex(pair[1], out var right);
        return OpticalKerning.Between(font, left, font.GetAdvanceWidth(left), right);
    }

    [Test]
    public void TheScriptsWhoseLettersStandApart_AreKernedOptically()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OpticalKerning.Applies("Привет, World! Ελλάδα 123", 0, 25), Is.True);
            Assert.That(OpticalKerning.Applies("नमस्ते", 0, 6), Is.False, "the headline joins the letters");
            Assert.That(OpticalKerning.Applies("日本語", 0, 3), Is.False, "a grid");
            Assert.That(OpticalKerning.Applies("مرحبا", 0, 5), Is.False, "joined");
        });
    }

    [TestCase("OTFFonts/SourceSans3-Regular.otf", "To")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf", "LT")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf", "AV")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf", "Yo")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf", "Wa")]
    [TestCase("TTFFonts/PlayfairDisplay-Regular.ttf", "To")]
    [TestCase("TTFFonts/PlayfairDisplay-Regular.ttf", "LT")]
    [TestCase("TTFFonts/PlayfairDisplay-Regular.ttf", "AV")]
    [TestCase("TTFFonts/PlayfairDisplay-Regular.ttf", "Yo")]
    [TestCase("TTFFonts/PlayfairDisplay-Regular.ttf", "AT")]
    public void APairTheFontKernsClose_IsDrawnCloser(string path, string pair)
    {
        var font = Font(path);

        Assert.That(Kern(font, pair), Is.LessThan(-0.01 * font.UnitsPerEm));
    }

    [TestCase("OTFFonts/SourceSans3-Regular.otf", "nn")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf", "HH")]
    [TestCase("OTFFonts/SourceSans3-Regular.otf", "ll")]
    [TestCase("TTFFonts/PlayfairDisplay-Regular.ttf", "nn")]
    [TestCase("TTFFonts/PlayfairDisplay-Regular.ttf", "HH")]
    [TestCase("TTFFonts/PlayfairDisplay-Regular.ttf", "ll")]
    public void StraightStems_KeepTheirSpacing(string path, string pair)
    {
        var font = Font(path);

        Assert.That(Math.Abs(Kern(font, pair)), Is.LessThanOrEqualTo(0.03 * font.UnitsPerEm));
    }

    [TestCase("OTFFonts/SourceSans3-Regular.otf", "AA")]
    [TestCase("TTFFonts/PlayfairDisplay-Regular.ttf", "AA")]
    public void TwoGlyphs_AreNeverDrawnIntoEachOther(string path, string pair)
    {
        var font = Font(path);

        Assert.That(Kern(font, pair), Is.GreaterThanOrEqualTo(0), "the feet of two As already stand close");
    }

    [Test]
    public void ABlank_MovesNothing()
    {
        var font = Font("OTFFonts/SourceSans3-Regular.otf");

        Assert.Multiple(() =>
        {
            Assert.That(Kern(font, "T "), Is.Zero);
            Assert.That(Kern(font, " o"), Is.Zero);
        });
    }
}
