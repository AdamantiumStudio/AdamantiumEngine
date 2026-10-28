using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Line height, line stacking, line spacing and the most lines, as WPF's and Avalonia's: a set line height is
/// the distance between baselines, its extra room split above and below the text; larger text raises its line unless
/// every line is the block's height; spacing goes between lines; lines past the most are not laid out, or the last ends
/// in an ellipsis.</summary>
[TestFixture]
public class LineHeightTests
{
    private const double FontSize = 20;

    private const string Prose = "Once upon a time there lived a king whose daughters were all beautiful, but the " +
                                 "youngest was so beautiful that the sun itself was astonished whenever it shone.";

    private static string FontPath(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", name);

    private static TextLayout Lay(string text, Action<TextLayout> setUp = null, double width = double.NaN,
        TextWrapping wrapping = TextWrapping.WrapByWords, TextTrimming trimming = TextTrimming.None,
        AttributedText attributed = null)
    {
        var typeface = Typeface.LoadFont(FontPath("SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        setUp?.Invoke(layout);
        var size = new Size<double>(width, double.NaN);
        if (attributed != null)
        {
            layout.ProcessText(attributed, FontSize, size, wrapping, trimming, HorizontalTextAlignment.Left,
                VerticalTextAlignment.Top);
        }
        else
        {
            layout.ProcessText(text, FontSize, size, wrapping, trimming, HorizontalTextAlignment.Left,
                VerticalTextAlignment.Top);
        }

        return layout;
    }

    [Test]
    public void ALineHeight_IsTheDistanceBetweenBaselines()
    {
        var layout = Lay("one\ntwo\nthree", set => set.LineHeight = 40);

        Assert.That(layout.GetLine(0).Height, Is.EqualTo(40).Within(1e-6));
        Assert.That(layout.GetLine(1).Baseline - layout.GetLine(0).Baseline, Is.EqualTo(40).Within(1));
        Assert.That(layout.GetLine(2).Top - layout.GetLine(1).Top, Is.EqualTo(40).Within(1e-6));
    }

    [Test]
    public void ItsExtraRoom_IsSplitAboveAndBelowTheText()
    {
        var natural = Lay("one\ntwo");
        var tall = Lay("one\ntwo", set => set.LineHeight = natural.GetLine(0).Height + 20);

        Assert.That(tall.GetLine(0).Baseline - natural.GetLine(0).Baseline, Is.EqualTo(10).Within(1));
        Assert.That(tall.GetTextData()[0].Rect.Y - natural.GetTextData()[0].Rect.Y, Is.EqualTo(10).Within(1));
    }

    [Test]
    public void LargerText_RaisesItsLine_UnlessEveryLineIsTheBlocks()
    {
        var attributed = new AttributedText("small BIG\nnext");
        attributed.Apply(6, 3, new TextAttributes { FontSize = 40 });
        var grows = Lay(null, set => set.LineHeight = 30, attributed: attributed);
        var block = Lay(null, set =>
        {
            set.LineHeight = 30;
            set.LineStacking = LineStackingStrategy.BlockLineHeight;
        }, attributed: attributed);

        Assert.That(grows.GetLine(0).Height, Is.GreaterThan(40));
        Assert.That(grows.GetLine(1).Height, Is.EqualTo(30).Within(1e-6));
        Assert.That(block.GetLine(0).Height, Is.EqualTo(30).Within(1e-6));
        Assert.That(block.GetLine(1).Top, Is.EqualTo(30).Within(1e-6));
    }

    [Test]
    public void AnotherFaceOfTheSameSize_LeavesATightLineTight()
    {
        var bold = Typeface.LoadFont(FontPath("SourceSans3-Bold.ttf")).Fonts[0];
        var attributed = new AttributedText("plain bold\nnext");
        attributed.Apply(6, 4, new TextAttributes { Font = bold });
        var layout = Lay(null, set => set.LineHeight = 18, attributed: attributed);

        Assert.That(layout.GetLine(0).Height, Is.EqualTo(18).Within(1));
    }

    [Test]
    public void LineSpacing_GoesBetweenLines_NotAfterTheLast()
    {
        var plain = Lay("one\ntwo\nthree");
        var spaced = Lay("one\ntwo\nthree", set => set.LineSpacing = 6);

        Assert.That(spaced.GetLine(1).Top - plain.GetLine(1).Top, Is.EqualTo(6).Within(1e-6));
        Assert.That(spaced.GetLine(2).Top - plain.GetLine(2).Top, Is.EqualTo(12).Within(1e-6));
        Assert.That(spaced.GetLine(1).Height, Is.EqualTo(plain.GetLine(1).Height).Within(1e-6));
        Assert.That(spaced.CalculatedLayoutSize.Height - plain.CalculatedLayoutSize.Height, Is.EqualTo(12).Within(1));
    }

    [Test]
    public void SpacedWrappedText_StandsOnTheSameLinesItIsMeasuredBy()
    {
        var layout = Lay(Prose, set => set.LineSpacing = 5, 200);
        var first = layout.GetTextData().First(glyph => glyph.LineIndex == 2 && glyph.Symbol != ' ');

        Assert.That(layout.LineCount, Is.GreaterThan(3));
        Assert.That(first.Rect.Bottom, Is.LessThanOrEqualTo(layout.GetLine(2).Top + layout.GetLine(2).Height));
        Assert.That(first.Rect.Y, Is.GreaterThanOrEqualTo(layout.GetLine(2).Top));
    }

    [Test]
    public void PastTheMostLines_NothingIsLaidOut()
    {
        var layout = Lay(Prose, set => set.MaxLines = 2, 200);

        Assert.That(layout.LineCount, Is.EqualTo(2));
        Assert.That(layout.GetTextData().Max(glyph => glyph.LineIndex), Is.EqualTo(1));
        Assert.That(layout.OversetIndex, Is.LessThan(Prose.Length));
        Assert.That(layout.GetTextData().Where(glyph => glyph.LineIndex == 1).Max(glyph => glyph.Rect.Right),
            Is.LessThanOrEqualTo(200 + 1e-3), "the last line does not run on past the edge");
    }

    [Test]
    public void Trimmed_TheLastOfTheMostLinesEndsInAnEllipsis()
    {
        var layout = Lay(Prose, set => set.MaxLines = 2, 200, trimming: TextTrimming.CharEllipses);

        Assert.That(layout.LineCount, Is.EqualTo(2));
        Assert.That(layout.GetTextData().Count(glyph => glyph.PositionInString < 0 && glyph.Symbol == '.'),
            Is.EqualTo(3));
    }

    [Test]
    public void TheMostLines_CountLinesTheTextEnds()
    {
        var layout = Lay("one\ntwo\nthree\nfour", set => set.MaxLines = 2);

        Assert.That(layout.LineCount, Is.EqualTo(2));
        Assert.That(layout.OversetIndex, Is.EqualTo(8));
    }

    [Test]
    public void TheMostLines_HoldWhenWrappedBySymbols()
    {
        var layout = Lay(Prose, set => set.MaxLines = 3, 150, TextWrapping.WrapBySymbols);

        Assert.That(layout.GetTextData().Max(glyph => glyph.LineIndex), Is.EqualTo(2));
    }

    [Test]
    public void ChangingOnlyTheLineHeight_LaysTheSameTextOutAgain()
    {
        var layout = Lay("one\ntwo");
        var before = layout.GetLine(1).Top;
        layout.LineHeight = 50;
        layout.ProcessText("one\ntwo", FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetLine(1).Top, Is.EqualTo(50).Within(1e-6));
        Assert.That(before, Is.Not.EqualTo(50).Within(1e-6));
    }

    [Test]
    public void TheCaret_StopsWhereTheMostLinesEnd()
    {
        var layout = Lay(Prose, set => set.MaxLines = 2, 200);

        Assert.That(layout.GetLine(1).End, Is.EqualTo(layout.OversetIndex));
    }

    [Test]
    public void Trimmed_AndHyphenated_TheLastLineEndsInAnEllipsis()
    {
        const string text = "Hyphenation of extraordinarily long incomprehensibilities and counterrevolutionaries";
        var layout = Lay(text, set =>
        {
            set.MaxLines = 2;
            set.Hyphens = Hyphens.Auto;
            set.Language = "en-US";
        }, 130, trimming: TextTrimming.CharEllipses);
        var last = layout.GetTextData().Where(glyph => glyph.LineIndex == 1).ToArray();

        Assert.That(layout.LineCount, Is.EqualTo(2));
        Assert.That(last.Count(glyph => glyph.PositionInString < 0 && glyph.Symbol == '.'), Is.EqualTo(3));
    }

    [Test]
    public void WrappedBySymbols_TheCutIsReported()
    {
        var layout = Lay(Prose, set => set.MaxLines = 3, 150, TextWrapping.WrapBySymbols);

        Assert.That(layout.OversetIndex, Is.LessThan(Prose.Length));
    }

    [Test]
    public void ATrailingNewline_AddsNoLinePastTheMost()
    {
        var layout = Lay("one\ntwo\n", set => set.MaxLines = 2);

        Assert.That(layout.LineCount, Is.EqualTo(2));
    }

    [Test]
    public void AFrame_HoldsTheLinesThatFitWithSpacingBetweenThem()
    {
        var natural = Lay("x").GetLine(0).Height;
        var layout = Lay(Prose, set =>
        {
            set.LineSpacing = 4;
            set.Frames = [new RectangleF(0, 0, 150, (float)(3 * natural + 2 * 4 + 0.5))];
        }, 150);

        Assert.That(layout.LineCount, Is.EqualTo(3));
    }

    [Test]
    public void AnUnusableSpacing_IsNone()
    {
        var plain = Lay("one\ntwo");
        var layout = Lay("one\ntwo", set => set.LineSpacing = double.NaN);

        Assert.That(layout.GetLine(1).Top, Is.EqualTo(plain.GetLine(1).Top).Within(1e-6));
    }

    [Test]
    public void FewerLinesThanTheMost_AreAllLaidOut()
    {
        var layout = Lay(Prose, set => set.MaxLines = 50, 200);

        Assert.That(layout.OversetIndex, Is.EqualTo(Prose.Length));
    }
}
