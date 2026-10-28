using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Justified text: lines fill the width and the last line of a paragraph stays at its start, on the right in a
/// right-to-left paragraph; the caret, a click and a selection reach across the widened gaps.</summary>
[TestFixture]
public class JustificationTests
{
    private const double FontSize = 20;
    private const double Width = 300;

    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone " +
        "in her face.";

    private const string Hebrew =
        "בראשית ברא אלהים את השמים ואת הארץ. והארץ היתה תהו ובהו וחשך על פני תהום ורוח אלהים מרחפת על פני המים.";

    private const string PointedHebrew =
        "בְּרֵאשִׁית בָּרָא אֱלֹהִים אֵת הַשָּׁמַיִם וְאֵת הָאָרֶץ. וְהָאָרֶץ הָיְתָה תֹהוּ וָבֹהוּ וְחֹשֶׁךְ עַל פְּנֵי תְהוֹם.";

    private static TextLayout Lay(string text, string font, LineBreaking breaking = LineBreaking.Greedy,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Justify)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", font));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, LineBreaking = breaking, EmitNewlineCarets = true,
        };
        layout.ProcessText(text, FontSize, new Size<double>(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            alignment, VerticalTextAlignment.Top);
        return layout;
    }

    [TestCase("SourceSans3-Regular.ttf", Prose, HorizontalTextAlignment.Left)]
    [TestCase("SourceSans3-Regular.ttf", Prose, HorizontalTextAlignment.Center)]
    [TestCase("SourceSans3-Regular.ttf", Prose, HorizontalTextAlignment.Right)]
    [TestCase("SourceSans3-Regular.ttf", Prose, HorizontalTextAlignment.Justify)]
    [TestCase("NotoSansHebrew-Regular.ttf", Hebrew, HorizontalTextAlignment.Center)]
    [TestCase("NotoSansHebrew-Regular.ttf", Hebrew, HorizontalTextAlignment.Right)]
    public void TheLastLine_StandsWhereLastLineAlignmentPutsIt(string font, string text, HorizontalTextAlignment last)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", font));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, LastLineAlignment = last };
        layout.ProcessText(text, FontSize, new Size<double>(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);
        var (left, right) = Ink(layout, layout.LineCount - 1);
        var rightToLeft = layout.IsRightToLeftParagraph(0);

        switch (last)
        {
            case HorizontalTextAlignment.Center:
                Assert.That((left + right) / 2, Is.EqualTo(Width / 2).Within(1));
                break;
            case HorizontalTextAlignment.Justify:
                Assert.That(left, Is.EqualTo(0).Within(2));
                Assert.That(right, Is.EqualTo(Width).Within(2));
                break;
            default:
                var atRight = (last == HorizontalTextAlignment.Right) != rightToLeft;
                Assert.That(atRight ? right : left, Is.EqualTo(atRight ? Width : 0).Within(2));
                Assert.That(right - left, Is.LessThan(Width - 20));
                break;
        }
    }

    private static TextLayout Justified(string text, string font, double width, HorizontalTextAlignment single,
        HorizontalTextAlignment last = HorizontalTextAlignment.Left, SpacingRange? letters = null, bool hangs = false)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", font));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, SingleWordJustification = single, LastLineAlignment = last,
            LetterSpacing = letters ?? SpacingRange.Letters, OpticalMarginAlignment = hangs,
        };
        layout.ProcessText(text, FontSize, new Size<double>(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);
        return layout;
    }

    [Test]
    public void ALineWithATab_IsNotASingleWord()
    {
        const string text = "Introduction\t17 Incomprehensibilities";
        var glyphs = Justified(text, "SourceSans3-Regular.ttf", 220, HorizontalTextAlignment.Justify).GetTextData();
        var one = Array.FindIndex(glyphs, glyph => glyph.Symbol == '1');

        Assert.That(glyphs[one].LineIndex, Is.EqualTo(0));
        Assert.That(glyphs[one + 1].Rect.Left - glyphs[one].Rect.Right, Is.LessThan(3), "the number stays whole");
    }

    [Test]
    public void ASingleWord_IsCentered_EvenWhenItsLettersTakeSomeOfTheRoom()
    {
        var layout = Justified("Incomprehensibilities notwithstanding, we go on.", "SourceSans3-Regular.ttf", 220,
            HorizontalTextAlignment.Center, letters: new SpacingRange(0, 0, 0.05));
        var (left, right) = Ink(layout, 0);

        Assert.That((left + right) / 2, Is.EqualTo(110).Within(1));
        Assert.That(left, Is.GreaterThan(2), "not left at the start");
    }

    [Test]
    public void ALastLineAtTheEnd_HangsItsPunctuationPastTheRightEdge()
    {
        var layout = Justified(Prose, "SourceSans3-Regular.ttf", Width, HorizontalTextAlignment.Left,
            HorizontalTextAlignment.Right, hangs: true);

        Assert.That(Ink(layout, layout.LineCount - 1).Right, Is.GreaterThan(Width + 1), "the final full stop hangs");
    }

    [Test]
    public void LinesOfIdeographs_AreNotSingleWords()
    {
        const string text = "吾輩は猫である。名前はまだ無い。どこで生れたかとんと見当がつかぬ。何でも薄暗いじめじめした所で泣いていた事だけは記憶している。";
        var layout = Justified(text, "NotoSansCJK-Regular.ttc", Width, HorizontalTextAlignment.Center);
        var (left, _) = Ink(layout, 0);

        Assert.That(layout.LineCount, Is.GreaterThan(1));
        Assert.That(left, Is.LessThan(3), "the first line starts at the start");
    }

    [Test]
    public void TheCaretOnAnEmptyLastLine_StaysAtTheStart_WhenTheLastLineIsJustified()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, EmitNewlineCarets = true, LastLineAlignment = HorizontalTextAlignment.Center,
        };
        layout.ProcessText("abc\n", FontSize, new Size<double>(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Justify, VerticalTextAlignment.Top, justifyLastLine: true);

        Assert.That(layout.GetCaretStops()[4].X, Is.EqualTo(0).Within(0.5));
    }

    [Test]
    public void TheCaretOnAnEmptyLastLineOfJustifiedText_FollowsLastLineAlignment()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, EmitNewlineCarets = true, LastLineAlignment = HorizontalTextAlignment.Center,
        };
        layout.ProcessText("abc\n", FontSize, new Size<double>(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);

        Assert.That(layout.GetCaretStops()[4].X, Is.EqualTo(Width / 2).Within(0.5));
    }

    [TestCase(HorizontalTextAlignment.Left)]
    [TestCase(HorizontalTextAlignment.Center)]
    [TestCase(HorizontalTextAlignment.Right)]
    [TestCase(HorizontalTextAlignment.Justify)]
    public void ASingleWordLine_StandsWhereSingleWordJustificationPutsIt(HorizontalTextAlignment single)
    {
        const string text = "Incomprehensibilities notwithstanding, we go on.";
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, SingleWordJustification = single };
        layout.ProcessText(text, FontSize, new Size<double>(220, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);
        var line = layout.GetLine(0);
        var (left, right) = Ink(layout, 0);

        Assert.That(text.Substring(line.Start, line.End - line.Start).Trim(), Is.EqualTo("Incomprehensibilities"));
        switch (single)
        {
            case HorizontalTextAlignment.Left:
                Assert.That(left, Is.EqualTo(0).Within(2));
                Assert.That(right, Is.LessThan(210));
                break;
            case HorizontalTextAlignment.Center:
                Assert.That((left + right) / 2, Is.EqualTo(110).Within(1));
                break;
            case HorizontalTextAlignment.Right:
                Assert.That(right, Is.EqualTo(220).Within(1));
                break;
            default:
                Assert.That(left, Is.EqualTo(0).Within(2));
                Assert.That(right, Is.EqualTo(220).Within(1), "its letters spread across the line");
                break;
        }
    }

    [TestCase(HorizontalTextAlignment.Left, 0)]
    [TestCase(HorizontalTextAlignment.Center, Width / 2)]
    [TestCase(HorizontalTextAlignment.Right, Width)]
    [TestCase(HorizontalTextAlignment.Justify, 0)]
    public void TheCaretOnAnEmptyLastLine_StandsWhereTheAlignmentPutsText(HorizontalTextAlignment alignment, double x)
    {
        var stops = Lay("abc\n", "SourceSans3-Regular.ttf", alignment: alignment).GetCaretStops();

        Assert.That(stops[4].X, Is.EqualTo(x).Within(0.5));
        Assert.That(stops[4].LineIndex, Is.EqualTo(1));
    }

    private static (double Left, double Right) Ink(TextLayout layout, int line)
    {
        var glyphs = layout.GetTextData().Where(glyph => glyph.LineIndex == line && glyph.Symbol != ' ').ToArray();
        return (glyphs.Min(glyph => glyph.Rect.Left), glyphs.Max(glyph => glyph.Rect.Right));
    }

    [TestCase("SourceSans3-Regular.ttf", Prose, LineBreaking.Greedy)]
    [TestCase("SourceSans3-Regular.ttf", Prose, LineBreaking.Paragraph)]
    [TestCase("NotoSansHebrew-Regular.ttf", Hebrew, LineBreaking.Greedy)]
    [TestCase("NotoSansHebrew-Regular.ttf", PointedHebrew, LineBreaking.Greedy)]
    public void LinesFillTheWidth_TheLastStaysAtItsStart(string font, string text, LineBreaking breaking)
    {
        var layout = Lay(text, font, breaking);
        var rightToLeft = layout.IsRightToLeftParagraph(0);
        var last = layout.LineCount - 1;

        Assert.That(last, Is.GreaterThan(1));
        for (var line = 0; line < last; line++)
        {
            var (left, right) = Ink(layout, line);
            Assert.That(left, Is.EqualTo(0).Within(2), $"line {line} starts at the left");
            Assert.That(right, Is.EqualTo(Width).Within(2), $"line {line} reaches the right");
        }

        var (lastLeft, lastRight) = Ink(layout, last);
        Assert.That(lastRight - lastLeft, Is.LessThan(Width - 20), "the last line is short");
        Assert.That(rightToLeft ? lastRight : lastLeft, Is.EqualTo(rightToLeft ? Width : 0).Within(2),
            "the last line stands at the start of the paragraph");
    }

    [TestCase("SourceSans3-Regular.ttf", Prose)]
    [TestCase("NotoSansHebrew-Regular.ttf", Hebrew)]
    public void CaretStops_LeaveNoGapsInAJustifiedLine(string font, string text)
    {
        var layout = Lay(text, font);
        var stops = layout.GetCaretStops();

        for (var line = 0; line < layout.LineCount - 1; line++)
        {
            var metrics = layout.GetLine(line);
            var covered = Enumerable.Range(metrics.Start, metrics.End - metrics.Start)
                .Select(index => stops[index]).OrderBy(stop => stop.Left).ToArray();
            for (var k = 1; k < covered.Length; k++)
            {
                Assert.That(covered[k].Left, Is.EqualTo(covered[k - 1].Right).Within(0.01),
                    $"line {line}: no gap before the stop at {covered[k].Left}");
            }
        }
    }

    [TestCase("SourceSans3-Regular.ttf", Prose)]
    [TestCase("NotoSansHebrew-Regular.ttf", Hebrew)]
    public void AClickInAWidenedGap_LandsOnItsSpace(string font, string text)
    {
        var layout = Lay(text, font);
        var stops = layout.GetCaretStops();
        var line = layout.GetLine(0);
        var space = Enumerable.Range(line.Start, line.End - line.Start).First(index => text[index] == ' ');
        var gapMiddle = (stops[space].Left + stops[space].Right) / 2;

        var hit = layout.HitTest(gapMiddle, line.Top + line.Height / 2);

        Assert.That(hit.Index, Is.EqualTo(space));
        Assert.That(hit.IsInside, Is.True);
        Assert.That(stops[space].Width, Is.GreaterThan(FontSize / 4), "the space is widened");
    }

    [Test]
    public void ASelectedLine_IsCoveredInOnePiece()
    {
        var layout = Lay(Prose, "SourceSans3-Regular.ttf");
        var line = layout.GetLine(0);

        var rects = layout.GetRangeRects(line.Start, line.End);

        Assert.That(rects, Has.Count.EqualTo(1));
        Assert.That(rects[0].Right, Is.GreaterThan(Width - 2));
    }
}
