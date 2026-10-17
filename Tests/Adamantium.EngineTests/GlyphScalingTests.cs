using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Glyph scaling and justification alternates, as InDesign justifies: glyphs drawn at their desired width, a
/// justified line widening or narrowing its glyphs once its spacing is at its limits, and taking a font's wider
/// justification forms ('jalt') before that.</summary>
[TestFixture]
public class GlyphScalingTests
{
    private const double FontSize = 20;
    private const double Width = 300;

    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone " +
        "in her face.";

    private static TextLayout Lay(string text, string fontPath, HorizontalTextAlignment alignment, SpacingRange scaling,
        SpacingRange? words = null, bool alternates = true, double width = Width,
        LineBreaking breaking = LineBreaking.Greedy)
    {
        var typeface = Typeface.LoadFont(fontPath);
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null,
            LineBreaking = breaking,
            GlyphScaling = scaling,
            WordSpacing = words ?? SpacingRange.Words,
            JustificationAlternates = alternates,
        };
        layout.ProcessText(text, FontSize, new Size(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            alignment, VerticalTextAlignment.Top);
        return layout;
    }

    private static string SourceSans => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf");

    [Test]
    public void DesiredScaling_DrawsAndAdvancesGlyphsWider()
    {
        var plain = Lay("Wide", SourceSans, HorizontalTextAlignment.Left, new SpacingRange(1, 1, 1), width: double.NaN)
            .GetTextData();
        var wide = Lay("Wide", SourceSans, HorizontalTextAlignment.Left, new SpacingRange(1.1, 1.1, 1.1), width: double.NaN)
            .GetTextData();

        for (var i = 0; i < plain.Length; i++)
        {
            Assert.That(wide[i].HorizontalScale, Is.EqualTo(1.1).Within(1e-9));
            Assert.That(wide[i].Advance, Is.EqualTo(plain[i].Advance * 1.1).Within(1e-4));
            Assert.That(wide[i].Rect.Width, Is.EqualTo(plain[i].Rect.Width * 1.1).Within(1e-3));
        }
    }

    [Test]
    public void DesiredScaling_KeepsAMarkOnItsLetter()
    {
        const string text = "и́";
        var plain = Lay(text, SourceSans, HorizontalTextAlignment.Left, new SpacingRange(1, 1, 1), width: double.NaN)
            .GetTextData();
        var wide = Lay(text, SourceSans, HorizontalTextAlignment.Left, new SpacingRange(1.2, 1.2, 1.2), width: double.NaN)
            .GetTextData();

        Assert.That(wide[1].Rect.Center.X - wide[0].Rect.X, Is.EqualTo((plain[1].Rect.Center.X - plain[0].Rect.X) * 1.2).Within(0.05));
    }

    [Test]
    public void AJustifiedLine_ScalesGlyphsOnlyOnceItsSpacesAreAtTheirMaximum()
    {
        var words = new SpacingRange(1, 1, 1.2);
        var scaling = new SpacingRange(1, 1, 1.04);
        var layout = Lay(Prose, SourceSans, HorizontalTextAlignment.Justify, scaling, words);
        var plain = Lay(Prose, SourceSans, HorizontalTextAlignment.Left, scaling, words).GetTextData();
        var glyphs = layout.GetTextData();
        var unit = layout.Font.GetAdvanceWidth(layout.Font.GetGlyphByCharacter(' ').Index) * FontSize / layout.Font.UnitsPerEm;
        var scaled = false;

        for (var line = 0; line < layout.LineCount - 1; line++)
        {
            var onLine = Enumerable.Range(0, glyphs.Length).Where(i => glyphs[i].LineIndex == line).ToArray();
            var lastInk = onLine.Last(i => glyphs[i].Symbol != ' ');
            var spaceGrowth = onLine.Where(i => i < lastInk && glyphs[i].Symbol == ' ')
                .Select(i => glyphs[i].Advance - plain[i].Advance).ToArray();
            var scales = onLine.Where(i => glyphs[i].Symbol != ' ').Select(i => glyphs[i].HorizontalScale).ToArray();

            Assert.That(scales, Is.All.InRange(1 - 1e-9, 1.04 + 1e-9), $"line {line}");
            Assert.That(glyphs.Where(g => g.LineIndex == line && g.Symbol != ' ').Max(g => g.Rect.Right),
                Is.EqualTo(Width).Within(1), $"line {line} fills the width");
            if (scales.Any(scale => scale > 1 + 1e-9))
            {
                scaled = true;
                Assert.That(spaceGrowth, Is.All.GreaterThanOrEqualTo(0.2 * unit - 1e-3), $"line {line}: spaces first");
            }
        }

        Assert.That(scaled, Is.True, "some line needed its glyphs widened");
    }

    [Test]
    public void AJustifiedLine_NarrowsItsGlyphsNoFurtherThanTheirMinimum()
    {
        var scaling = new SpacingRange(0.95, 1, 1);
        var narrowed = false;
        for (var width = 240; width <= 360; width += 10)
        {
            var glyphs = Lay(Prose, SourceSans, HorizontalTextAlignment.Justify, scaling, new SpacingRange(1, 1, 1.33),
                width: width, breaking: LineBreaking.Paragraph).GetTextData();

            Assert.That(glyphs.Where(glyph => glyph.Symbol != ' ').Select(glyph => glyph.HorizontalScale),
                Is.All.InRange(0.95 - 1e-9, 1 + 1e-9), $"width {width}");
            Assert.That(glyphs.Where(glyph => glyph.Symbol != ' ').Max(glyph => glyph.Rect.Right),
                Is.LessThanOrEqualTo(width + 0.5), $"width {width}");
            narrowed |= glyphs.Any(glyph => glyph.HorizontalScale < 1 - 1e-6);
        }

        Assert.That(narrowed, Is.True, "some line fit by narrowing its glyphs");
    }

    [Test]
    public void TrimmedJustifiedText_EndsInAnEllipsisOnlyWhereItIsCut()
    {
        for (var width = 200; width <= 400; width += 5)
        {
            var typeface = Typeface.LoadFont(SourceSans);
            var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
            layout.ProcessText(Prose, FontSize, new Size(width, double.NaN), TextWrapping.WrapByWords,
                TextTrimming.CharEllipses, HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);

            Assert.That(layout.GetTextData().Count(glyph => glyph.PositionInString < 0 && glyph.Symbol == '.'), Is.Zero,
                $"width {width}: the whole text fits, so nothing ends in an ellipsis");
        }
    }

    [Test]
    public void ScaledLines_FillTheWidthExactly_WithTracking()
    {
        var typeface = Typeface.LoadFont(SourceSans);
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, Tracking = 60, GlyphScaling = new SpacingRange(1, 1, 1.1), WordSpacing = new SpacingRange(1, 1, 1.05),
        };
        layout.ProcessText(Prose, FontSize, new Size(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);
        var glyphs = layout.GetTextData();

        Assert.That(glyphs.Any(glyph => glyph.HorizontalScale > 1 + 1e-9), Is.True, "some line widened its glyphs");
        for (var line = 0; line < layout.LineCount - 1; line++)
        {
            Assert.That(glyphs.Where(glyph => glyph.LineIndex == line && glyph.Symbol != ' ').Max(glyph => glyph.Rect.Right),
                Is.EqualTo(Width).Within(0.5), $"line {line} fills the width");
        }
    }

    [Test]
    public void GlyphScaling_MustStayAboveZero()
    {
        var typeface = Typeface.LoadFont(SourceSans);
        var layout = new TextLayout(typeface, typeface.Fonts[0]);

        Assert.Throws<ArgumentOutOfRangeException>(() => layout.GlyphScaling = new SpacingRange(0, 1, 1));
    }

    [Test]
    public void AHyphen_IsScaledWithItsWord()
    {
        var typeface = Typeface.LoadFont(SourceSans);
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, Hyphens = Hyphens.Auto, Language = "en-US", GlyphScaling = new SpacingRange(1.1, 1.1, 1.1),
        };
        layout.ProcessText(Prose, FontSize, new Size(120, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var hyphens = layout.GetTextData().Where(glyph => glyph.PositionInString < 0 && glyph.Symbol == '-').ToArray();

        Assert.That(hyphens, Is.Not.Empty);
        Assert.That(hyphens.Select(glyph => glyph.HorizontalScale), Is.All.EqualTo(1.1).Within(1e-9));
    }

    [Test]
    public void ScaledJustification_LeavesNoGapsForTheCaret()
    {
        var layout = Lay(Prose, SourceSans, HorizontalTextAlignment.Justify, new SpacingRange(0.97, 1, 1.03),
            new SpacingRange(1, 1, 1.1));
        var stops = layout.GetCaretStops();

        for (var line = 0; line < layout.LineCount - 1; line++)
        {
            var metrics = layout.GetLine(line);
            var covered = Enumerable.Range(metrics.Start, metrics.End - metrics.Start)
                .Select(index => stops[index]).OrderBy(stop => stop.Left).ToArray();
            for (var k = 1; k < covered.Length; k++)
            {
                Assert.That(covered[k].Left, Is.EqualTo(covered[k - 1].Right).Within(0.01), $"line {line}");
            }
        }
    }

    // The font's 'jalt' turns a into m and а into м, for Cyrillic text only.
    [TestCase("a banana and a cat, a hat and a bag, a man and a can, a jam and a map, a fan and a pan", false)]
    [TestCase("а банан и сад, а там дама, а вам каша, а нам мама, а сам Сава, а там папа", true)]
    public void JustificationAlternates_AreTakenOnlyFromTheTextsScript(string text, bool replaces)
    {
        var font = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "JaltByScript-Regular.ttf");
        var plain = Lay(text, font, HorizontalTextAlignment.Justify, new SpacingRange(1, 1, 1), new SpacingRange(1, 1, 1),
            alternates: false, width: 200).GetTextData();
        var wide = Lay(text, font, HorizontalTextAlignment.Justify, new SpacingRange(1, 1, 1), new SpacingRange(1, 1, 1),
            width: 200).GetTextData();

        Assert.That(Enumerable.Range(0, plain.Length).Any(i => wide[i].Glyph.Index != plain[i].Glyph.Index),
            Is.EqualTo(replaces));
    }

    // The Culmus fonts' wide Hebrew letters come with some Windows installations; the test runs where they are.
    [Test]
    public void AJustifiedHebrewLine_TakesTheFontsWideLetters()
    {
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "DavidCLM-Medium.otf");
        if (!File.Exists(font))
        {
            Assert.Ignore("the Culmus font David CLM is not installed");
        }

        const string text = "בראשית ברא אלהים את השמים ואת הארץ. והארץ היתה תהו ובהו וחשך על פני תהום.";
        var words = new SpacingRange(1, 1, 1.1);
        var plain = Lay(text, font, HorizontalTextAlignment.Justify, new SpacingRange(1, 1, 1), words, alternates: false)
            .GetTextData();
        var wide = Lay(text, font, HorizontalTextAlignment.Justify, new SpacingRange(1, 1, 1), words).GetTextData();

        Assert.That(wide.Select(glyph => glyph.LineIndex), Is.EqualTo(plain.Select(glyph => glyph.LineIndex)));
        var replaced = Enumerable.Range(0, plain.Length).Where(i => wide[i].Glyph.Index != plain[i].Glyph.Index).ToArray();
        Assert.That(replaced, Is.Not.Empty, "some letters took their wide forms");
        foreach (var i in replaced)
        {
            Assert.That(wide[i].Font.GetGlyphAlternates(plain[i].Glyph.Index)
                .Any(alternate => alternate.Feature == "jalt" && alternate.Glyph == wide[i].Glyph.Index), Is.True);
        }
    }
}
