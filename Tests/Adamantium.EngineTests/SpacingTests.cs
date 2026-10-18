using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Word and letter spacing as InDesign's justification sets them, and tracking: spaces and letters take their
/// desired widths as laid out, a justified line stretches its spaces to their maximum before spacing its letters, and
/// joined scripts are never spaced.</summary>
[TestFixture]
public class SpacingTests
{
    private const double FontSize = 20;
    private const double Width = 300;

    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone " +
        "in her face.";

    private static TextLayout Lay(string text, string font = "SourceSans3-Regular.ttf",
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left, SpacingRange? words = null,
        SpacingRange? letters = null, double tracking = 0, LineBreaking breaking = LineBreaking.Greedy,
        double width = double.NaN)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", font));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null,
            LineBreaking = breaking,
            WordSpacing = words ?? SpacingRange.Words,
            LetterSpacing = letters ?? SpacingRange.Letters,
            Tracking = tracking,
        };
        layout.ProcessText(text, FontSize, new Size(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            alignment, VerticalTextAlignment.Top);
        return layout;
    }

    private static double Space(TextLayout layout) =>
        layout.Font.GetAdvanceWidth(layout.Font.GetGlyphByCharacter(' ').Index) * FontSize / layout.Font.UnitsPerEm;

    [Test]
    public void Tracking_AddsToEachCharacter_NotToItsMarks()
    {
        const string text = "aé b";
        var plain = Lay(text).GetTextData();
        var tracked = Lay(text, tracking: 100).GetTextData();

        Assert.That(tracked.Length, Is.EqualTo(plain.Length));
        for (var i = 0; i < plain.Length; i++)
        {
            var mark = plain[i].Advance == 0;
            Assert.That(tracked[i].Advance - plain[i].Advance, Is.EqualTo(mark ? 0 : FontSize / 10).Within(1e-6),
                $"glyph {i} ({plain[i].Symbol})");
        }
    }

    [TestCase(100)]
    [TestCase(-60)]
    public void Tracking_LeavesAMarkOnItsLetter(double tracking)
    {
        const string text = "и́ и";
        var plain = Lay(text).GetTextData();
        var tracked = Lay(text, tracking: tracking).GetTextData();
        var mark = Array.FindIndex(plain, glyph => glyph.Advance == 0 && glyph.Symbol != ' ');

        Assert.That(mark, Is.GreaterThan(0), "the mark is a glyph of its own");
        Assert.That(tracked[mark].Rect.X - tracked[mark - 1].Rect.X,
            Is.EqualTo(plain[mark].Rect.X - plain[mark - 1].Rect.X).Within(1e-3));
    }

    [Test]
    public void AJustifiedEmptyLine_IsLaidOut()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, EmitNewlineCarets = true, LetterSpacing = new SpacingRange(-0.1, 0, 0.5),
        };

        Assert.DoesNotThrow(() => layout.ProcessText("one two\n\n   \nthree four", FontSize, new Size(Width, double.NaN),
            TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Justify, VerticalTextAlignment.Top));
        Assert.That(layout.LineCount, Is.EqualTo(4));
    }

    [Test]
    public void UprightDigits_StayTogether_WhenAJustifiedVerticalLineSpacesItsLetters()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansCJK-Regular.ttc"));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, WritingMode = WritingMode.VerticalRightToLeft, LetterSpacing = new SpacingRange(0, 0, 0.5),
        };
        layout.ProcessText("漢字12月漢字漢字漢字漢字漢字", FontSize, new Size(200, 210), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);
        var glyphs = layout.GetTextData();
        var first = Array.FindIndex(glyphs, glyph => glyph.Symbol == '1');

        Assert.That(glyphs[first].LineIndex, Is.EqualTo(0));
        Assert.That(glyphs[first + 1].Rect.Top, Is.EqualTo(glyphs[first].Rect.Top).Within(1), "side by side, not one under the other");
        Assert.That(glyphs[first + 1].Rect.Left, Is.GreaterThan(glyphs[first].Rect.Right - 1));
    }

    [Test]
    public void ARangesTracking_TakesPrecedence()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, Tracking = 100 };
        var text = new AttributedText("ab", new TextAttributes { Tracking = -50 });
        layout.ProcessText(text, FontSize, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        var plain = Lay("ab").GetTextData();
        Assert.That(layout.GetTextData()[0].Advance - plain[0].Advance, Is.EqualTo(-FontSize / 20).Within(1e-6));
    }

    [Test]
    public void DesiredSpacing_WidensSpacesAndLetters()
    {
        var plain = Lay("ab cd");
        var spaced = Lay("ab cd", words: new SpacingRange(1, 2, 3), letters: new SpacingRange(0, 0.25, 0.5));
        var unit = Space(plain);

        var before = plain.GetTextData();
        var after = spaced.GetTextData();
        for (var i = 0; i < before.Length; i++)
        {
            var expected = (before[i].Symbol == ' ' ? 2 * before[i].Advance : before[i].Advance) + 0.25 * unit;
            Assert.That(after[i].Advance, Is.EqualTo(expected).Within(1e-6), $"glyph {i} ({before[i].Symbol})");
        }
    }

    [Test]
    public void JoinedScripts_AreNeverSpaced()
    {
        const string text = "مرحبا بالعالم";
        var plain = Lay(text, "NotoSansArabic-Regular.ttf").GetTextData();
        var spaced = Lay(text, "NotoSansArabic-Regular.ttf", letters: new SpacingRange(0, 0.3, 0.5), tracking: 200)
            .GetTextData();

        for (var i = 0; i < plain.Length; i++)
        {
            if (plain[i].Symbol != ' ')
            {
                Assert.That(spaced[i].Advance, Is.EqualTo(plain[i].Advance).Within(1e-6), $"glyph {i}");
            }
        }
    }

    // Lines broken alike (no squeezing), so a justified glyph's advance against the same glyph laid out flush left is
    // what justification gave it.
    [Test]
    public void AJustifiedLine_StretchesSpacesToTheirMaximum_ThenSpacesLetters()
    {
        var words = new SpacingRange(1, 1, 1.33);
        var letters = new SpacingRange(0, 0, 0.5);
        var left = Lay(Prose, words: words, letters: letters, width: Width);
        var justified = Lay(Prose, alignment: HorizontalTextAlignment.Justify, words: words, letters: letters, width: Width);
        var unit = Space(left);
        var before = left.GetTextData();
        var after = justified.GetTextData();
        var lastLine = before.Max(glyph => glyph.LineIndex);
        var lettersUsed = false;

        Assert.That(after.Select(glyph => glyph.LineIndex), Is.EqualTo(before.Select(glyph => glyph.LineIndex)));
        for (var line = 0; line < lastLine; line++)
        {
            var indices = Enumerable.Range(0, before.Length).Where(i => before[i].LineIndex == line).ToArray();
            var lastLetter = indices.Last(i => before[i].Symbol != ' ');
            var inner = indices.Where(i => i < lastLetter).ToArray();
            var spaceGrowth = inner.Where(i => before[i].Symbol == ' ').Select(i => after[i].Advance - before[i].Advance).ToArray();
            var letterGrowth = inner.Where(i => before[i].Symbol != ' ').Select(i => after[i].Advance - before[i].Advance).ToArray();

            Assert.That(after.Where((_, i) => indices.Contains(i) && after[i].Symbol != ' ').Max(glyph => glyph.Rect.Right),
                Is.EqualTo(Width).Within(1), $"line {line} fills the width");
            Assert.That(letterGrowth, Is.All.LessThanOrEqualTo(0.5 * unit + 1e-6), $"line {line}: letters within their maximum");
            if (spaceGrowth.Any(growth => growth > 0.33 * unit + 1e-6))
            {
                Assert.That(letterGrowth, Is.All.EqualTo(0.5 * unit).Within(1e-6),
                    $"line {line}: spaces past their maximum only once letters are at theirs");
            }

            if (letterGrowth.Any(growth => growth > 1e-6))
            {
                lettersUsed = true;
                Assert.That(spaceGrowth, Is.All.GreaterThanOrEqualTo(0.33 * unit - 1e-3),
                    $"line {line}: letters spaced only once spaces are at their maximum");
            }
        }

        Assert.That(lettersUsed, Is.True, "some line needed its letters spaced");
    }

    [Test]
    public void AJustifiedLine_SqueezesItsSpacesNoFurtherThanTheirMinimum()
    {
        var words = new SpacingRange(0.8, 1, 1.33);
        var squeezed = Lay(Prose, alignment: HorizontalTextAlignment.Justify, words: words, width: Width);
        var unit = Space(squeezed);
        var natural = Lay(Prose, words: new SpacingRange(1, 1, 1), width: double.NaN).GetTextData();
        var glyphs = squeezed.GetTextData();

        foreach (var glyph in glyphs.Where(glyph => glyph.Symbol == ' '))
        {
            var original = natural.First(other => other.PositionInString == glyph.PositionInString).Advance;
            Assert.That(glyph.Advance, Is.GreaterThanOrEqualTo(original - 0.2 * unit - 1e-6));
        }

        Assert.That(glyphs.Where(glyph => glyph.Symbol != ' ').Max(glyph => glyph.Rect.Right), Is.LessThanOrEqualTo(Width + 0.5));
    }

    [Test]
    public void ALineAtATime_TakesAWordItFitsBySqueezingItsSpaces()
    {
        var squeezed = false;
        for (var width = 200; width <= 400; width += 5)
        {
            var left = Lay(Prose, words: SpacingRange.Words, width: width);
            var justified = Lay(Prose, alignment: HorizontalTextAlignment.Justify, words: SpacingRange.Words, width: width);

            Assert.That(justified.LineCount, Is.LessThanOrEqualTo(left.LineCount), $"width {width}");
            Assert.That(justified.GetTextData().Where(glyph => glyph.Symbol != ' ').Max(glyph => glyph.Rect.Right),
                Is.LessThanOrEqualTo(width + 0.5), $"width {width}");
            squeezed |= justified.GetLine(0).End > left.GetLine(0).End;
        }

        Assert.That(squeezed, Is.True, "a first line took one more word by squeezing its spaces");
    }

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void LetterSpacedJustification_LeavesNoGapsForTheCaret(LineBreaking breaking)
    {
        var layout = Lay(Prose, alignment: HorizontalTextAlignment.Justify, letters: new SpacingRange(-0.1, 0, 0.5),
            breaking: breaking, width: Width);
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

            var ink = layout.GetTextData().Where(glyph => glyph.LineIndex == line && glyph.Symbol != ' ');
            Assert.That(ink.Max(glyph => glyph.Rect.Right), Is.EqualTo(Width).Within(1), $"line {line} fills the width");
        }
    }

    [Test]
    public void ASpacingRange_RunsFromItsMinimumToItsMaximum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpacingRange(1, 0.8, 1.33));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new SpacingRange(0.8, 1, double.NaN));
        Assert.That(SpacingRange.Words.ToString(), Is.EqualTo("80% 100% 133%"));
    }

    [TestCase("80% 100% 133%", 0.8, 1, 1.33)]
    [TestCase("0,0,50", 0, 0, 0.5)]
    [TestCase("-10% 0% 25%", -0.1, 0, 0.25)]
    [TestCase("100%", 1, 1, 1)]
    [TestCase("7% 7% 7%", 0.07, 0.07, 0.07)]
    public void Parse_ReadsPercents(string text, double minimum, double desired, double maximum)
    {
        var range = SpacingRange.Parse(text);

        Assert.That(range.Minimum, Is.EqualTo(minimum).Within(1e-9));
        Assert.That(range.Desired, Is.EqualTo(desired).Within(1e-9));
        Assert.That(range.Maximum, Is.EqualTo(maximum).Within(1e-9));
        Assert.That(SpacingRange.Parse(range.ToString()), Is.EqualTo(range));
    }

    [TestCase("80% 100%")]
    [TestCase("a b c")]
    [TestCase("120% 100% 133%")]
    public void Parse_RejectsWhatIsNotARange(string text)
    {
        Assert.Throws<FormatException>(() => SpacingRange.Parse(text));
    }

    // Ideographs stand with no space between them: the paragraph composer knew none of the letter spacing at their gaps
    // and could not close them up to take one more onto a line.
    [Test]
    public void TheParagraphComposer_ClosesUpTheGapsBetweenIdeographs()
    {
        const string text = "日本語の文章日本語の文章日本語の文章日本語の文章";
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "NotoSansCJK-Regular.ttc"));
        TextLayout Lay(LineBreaking breaking)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, LineBreaking = breaking, LetterSpacing = new SpacingRange(-0.5, 0, 0.5),
            };
            layout.ProcessText(text, 20, new Size(196, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);
            return layout;
        }

        var greedy = Lay(LineBreaking.Greedy);
        var composed = Lay(LineBreaking.Paragraph);

        Assert.That(greedy.GetLine(0).End - greedy.GetLine(0).Start, Is.EqualTo(10), "the greedy breaker closes them up");
        Assert.That(composed.GetLine(0).End - composed.GetLine(0).Start, Is.EqualTo(10), "ten, their gaps closed up");
        Assert.That(composed.GetTextData().Where(g => g.LineIndex == 0).Max(g => g.Rect.Right), Is.LessThanOrEqualTo(196.5));
    }
}
