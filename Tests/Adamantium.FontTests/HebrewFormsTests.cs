using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>Hebrew in a font with no GPOS marks: a letter and its point are set as the presentation form the font has,
/// as HarfBuzz composes them (glyphs checked against it, the subset of Noto Sans Hebrew with GPOS dropped); a font that
/// positions its marks keeps them apart.</summary>
[TestFixture]
public class HebrewFormsTests
{
    private const string NoGpos = "ScriptFonts/NotoSansHebrew-NoGpos.subset.ttf";

    [TestCase("שּׁ", 0xFB2C)]
    [TestCase("שּׂ", 0xFB2D)]
    [TestCase("בּ", 0xFB31)]
    [TestCase("וֹ", 0xFB4B)]
    [TestCase("אַ", 0xFB2E)]
    [TestCase("אָ", 0xFB2F)]
    [TestCase("יִ", 0xFB1D)]
    [TestCase("פֿ", 0xFB4E)]
    [TestCase("ײַ", 0xFB1F)]
    public void ALetterAndItsPoint_AreSetAsTheirPresentationForm(string text, int form)
    {
        var font = Typeface.LoadFont(NoGpos, 3).GetFont(0);
        Assume.That(font.TryGetGlyphIndex(form, out var expected), Is.True);

        var shaped = TextShaper.Shape(font, text);

        Assert.That(shaped.Select(glyph => glyph.GlyphIndex), Is.EqualTo(new[] { expected }));
    }

    [Test]
    public void ALetterWithNoPresentationForm_KeepsItsPoint()
    {
        var font = Typeface.LoadFont(NoGpos, 3).GetFont(0);

        Assert.That(TextShaper.Shape(font, "חּ"), Has.Length.EqualTo(2), "het takes no dagesh form");
    }

    [Test]
    public void AFontThatPositionsItsMarks_KeepsThemApart()
    {
        var font = Typeface.LoadFont("ScriptFonts/NotoSansHebrew-Regular.ttf", 3).GetFont(0);

        Assert.That(TextShaper.Shape(font, "בּ"), Has.Length.EqualTo(2));
    }
}
