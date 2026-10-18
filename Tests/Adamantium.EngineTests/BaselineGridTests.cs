using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A baseline grid: every line's baseline stands on a grid line at or below where it would stand, lines of a
/// larger size included; only first lines of paragraphs with <see cref="BaselineGrid.FirstLineOnly"/>; the columns of a
/// text level with one another; a whole-text shift keeps the rhythm; vertical text is left as it is.</summary>
[TestFixture]
public class BaselineGridTests
{
    private const double FontSize = 16;
    private const double Step = 24;
    private static readonly string ParagraphEnd = ((char)0x2029).ToString();

    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone " +
        "in her face.";

    private static TextLayout Layout(BaselineGrid grid, WritingMode mode = WritingMode.Horizontal)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        return new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, BaselineGrid = grid, WritingMode = mode };
    }

    private static TextLayout Lay(string text, BaselineGrid grid, double width = 300, double height = double.NaN,
        VerticalTextAlignment alignment = VerticalTextAlignment.Top, AttributedText attributed = null)
    {
        var layout = Layout(grid);
        if (attributed != null)
        {
            layout.ProcessText(attributed, FontSize, new Size(width, height), TextWrapping.WrapByWords,
                TextTrimming.None, HorizontalTextAlignment.Left, alignment);
        }
        else
        {
            layout.ProcessText(text, FontSize, new Size(width, height), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, alignment);
        }

        return layout;
    }

    private static double[] Baselines(TextLayout layout) =>
        Enumerable.Range(0, layout.LineCount).Select(i => layout.GetLine(i).Baseline).ToArray();

    private static bool OnGrid(double baseline, double offset = 0)
    {
        var phase = ((baseline - offset) % Step + Step) % Step;
        return phase < 0.51 || Step - phase < 0.51;
    }

    [Test]
    public void EveryBaseline_StandsOnTheGrid_NoHigherThanItWould()
    {
        var plain = Baselines(Lay(Prose, null));
        var gridded = Baselines(Lay(Prose, new BaselineGrid(Step)));

        Assert.Multiple(() =>
        {
            Assert.That(gridded.Length, Is.EqualTo(plain.Length).And.GreaterThan(2));
            Assert.That(gridded.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", gridded));
            Assert.That(gridded[0], Is.GreaterThanOrEqualTo(plain[0] - 0.5), "never moved up");
            for (var i = 1; i < gridded.Length; i++)
            {
                Assert.That(gridded[i] - gridded[i - 1], Is.EqualTo(Step).Within(0.51), "one step a line");
            }
        });
    }

    [Test]
    public void TheGridsOffset_IsWhereItsLinesLie()
    {
        var gridded = Baselines(Lay(Prose, new BaselineGrid(Step, 5)));

        Assert.That(gridded.All(baseline => OnGrid(baseline, 5)), Is.True, string.Join(", ", gridded));
    }

    [Test]
    public void ALineOfLargerText_StandsOnTheGrid_AndSoDoTheLinesAfterIt()
    {
        const string text = "A first line.\nA LARGER LINE\nAnd a third.";
        var attributed = new AttributedText(text).Apply(14, 13, new TextAttributes { FontSize = FontSize * 2 });

        var gridded = Baselines(Lay(text, new BaselineGrid(Step), attributed: attributed));

        Assert.Multiple(() =>
        {
            Assert.That(gridded.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", gridded));
            Assert.That(gridded[1] - gridded[0], Is.GreaterThan(Step), "the larger line takes more than one step");
        });
    }

    [Test]
    public void OnlyFirstLines_KeepTheRestAsTheyAre()
    {
        var text = Prose + ParagraphEnd + Prose;
        var plain = Lay(text, null);
        var gridded = Lay(text, new BaselineGrid(Step, FirstLineOnly: true));
        var plainLines = Baselines(plain);
        var lines = Baselines(gridded);
        var second = Enumerable.Range(0, gridded.LineCount).First(i => gridded.GetLine(i).Start > Prose.Length);

        Assert.Multiple(() =>
        {
            Assert.That(OnGrid(lines[0]) && OnGrid(lines[second]), Is.True, "the paragraphs' first lines");
            Assert.That(lines[1] - lines[0], Is.EqualTo(plainLines[1] - plainLines[0]).Within(0.51),
                "a later line keeps its spacing");
        });
    }

    [Test]
    public void TheColumnsOfAText_StandLevel()
    {
        var layout = Layout(new BaselineGrid(Step));
        layout.Frames = [new RectangleF(0, 7, 140, 200), new RectangleF(160, 0, 140, 200)];
        layout.ProcessText(Prose + " " + Prose, FontSize, new Size(300, 200), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var lines = Baselines(layout);

        Assert.That(lines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", lines));
    }

    [TestCase(VerticalTextAlignment.Center)]
    [TestCase(VerticalTextAlignment.Bottom)]
    public void AlignedInATallerArea_TheTextKeepsTheGrid(VerticalTextAlignment alignment)
    {
        var lines = Baselines(Lay(Prose, new BaselineGrid(Step), height: 500, alignment: alignment));

        Assert.That(lines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", lines));
    }

    [TestCase(TextTrimming.None)]
    [TestCase(TextTrimming.CharEllipses)]
    public void InAFixedHeight_EveryLineThatIsKeptStartsInside(TextTrimming trimming)
    {
        var layout = Layout(new BaselineGrid(Step));
        layout.ProcessText(Prose, FontSize, new Size(300, 50), TextWrapping.WrapByWords, trimming,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var tops = Enumerable.Range(0, layout.LineCount).Select(i => layout.GetLine(i).Top).ToArray();

        Assert.That(tops.All(top => top < 50), Is.True, string.Join(", ", tops));
    }

    [Test]
    public void InFrames_ALineOfLargerText_StandsOnTheGrid()
    {
        const string text = "A first line.\nA LARGER LINE\nAnd a third.\nAnd a fourth.";
        var attributed = new AttributedText(text).Apply(14, 13, new TextAttributes { FontSize = FontSize * 2 });
        var layout = Layout(new BaselineGrid(Step));
        layout.Frames = [new RectangleF(0, 0, 300, 400)];
        layout.ProcessText(attributed, FontSize, new Size(300, 400), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var lines = Baselines(layout);

        Assert.Multiple(() =>
        {
            Assert.That(lines.All(baseline => OnGrid(baseline)), Is.True, string.Join(", ", lines));
            for (var i = 1; i < layout.LineCount; i++)
            {
                Assert.That(layout.GetLine(i).Top, Is.GreaterThanOrEqualTo(
                    layout.GetLine(i - 1).Top + layout.GetLine(i - 1).Height - 1), $"line {i} is below the one before");
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InFrames_ALargerLine_PushesTheNextOnesDown_WithinTheirFramesAndPastExclusions(bool gridded)
    {
        var text = "Line one.\nA LARGER LINE\n" + Prose;
        var attributed = new AttributedText(text).Apply(10, 13, new TextAttributes { FontSize = FontSize * 2 });
        var exclusion = new RectangleF(0, 72, 200, 12);
        var layout = Layout(gridded ? new BaselineGrid(Step) : null);
        layout.Frames = [new RectangleF(0, 0, 300, 100), new RectangleF(320, 0, 300, 400)];
        layout.Exclusions = [exclusion];
        layout.ProcessText(attributed, FontSize, new Size(620, 400), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var glyphs = layout.GetTextData();

        bool InFirstFrame(int line) => glyphs.Where(glyph => glyph.LineIndex == line).All(glyph => glyph.Rect.X < 310);

        Assert.Multiple(() =>
        {
            for (var i = 0; i < layout.LineCount; i++)
            {
                var line = layout.GetLine(i);
                if (InFirstFrame(i))
                {
                    Assert.That(line.Top + line.Height, Is.LessThanOrEqualTo(100.5), $"line {i} ends in its frame");
                }

                if (i > 0 && InFirstFrame(i) == InFirstFrame(i - 1))
                {
                    var before = layout.GetLine(i - 1);
                    Assert.That(line.Top, Is.GreaterThanOrEqualTo(before.Top + before.Height - 1),
                        $"line {i} is below the one before");
                }

                var overlapsExclusion = line.Top < exclusion.Bottom && line.Top + line.Height > exclusion.Top
                                        && glyphs.Any(glyph => glyph.LineIndex == i && glyph.Rect.Width > 0
                                                               && glyph.Rect.Left < exclusion.Right);
                Assert.That(overlapsExclusion, Is.False, $"line {i}, the larger one too, keeps out of the exclusion");
            }

            if (gridded)
            {
                Assert.That(Baselines(layout).All(baseline => OnGrid(baseline)), Is.True,
                    string.Join(", ", Baselines(layout)));
            }
        });
    }

    [Test]
    public void InOneTallFrame_TheLinesStandWhereTheyDoWithoutFrames()
    {
        var text = "Line one.\nA LARGER LINE\n" + Prose;
        double[] Laid(bool framed)
        {
            var attributed = new AttributedText(text).Apply(10, 13, new TextAttributes { FontSize = FontSize * 2 });
            var layout = Layout(new BaselineGrid(Step, 3));
            layout.LineHeight = 30;
            if (framed)
            {
                layout.Frames = [new RectangleF(0, 0, 220, 2000)];
            }

            layout.ProcessText(attributed, FontSize, new Size(220, framed ? 2000 : double.NaN), TextWrapping.WrapByWords,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return Baselines(layout);
        }

        Assert.That(Laid(true), Is.EqualTo(Laid(false)).Within(0.01));
    }

    [Test]
    public void AFarOffset_KeepsTheLinesApart()
    {
        var near = Baselines(Lay(Prose, new BaselineGrid(Step, 5)));
        var far = Baselines(Lay(Prose, new BaselineGrid(Step, 5 + Step * 1e14)));

        Assert.That(far, Is.EqualTo(near).Within(0.51));
    }

    [Test]
    public void TheOnlyFrame_KeepsALineTheGridMovedPastItsBottom()
    {
        var layout = Layout(new BaselineGrid(Step));
        layout.Frames = [new RectangleF(0, 0, 300, 23)];
        layout.ProcessText(Prose, FontSize, new Size(300, 23), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.LineCount, Is.EqualTo(1));
        Assert.That(layout.GetTextData(), Is.Not.Empty);
    }

    [TestCase(LineBreaking.Greedy, false)]
    [TestCase(LineBreaking.Paragraph, false)]
    [TestCase(LineBreaking.Paragraph, true)]
    public void InFrames_OnlyFirstLines_StandOnTheGrid(LineBreaking breaking, bool paragraphRuns)
    {
        var text = Prose + (paragraphRuns ? ParagraphEnd : "\n") + Prose;
        var attributed = new AttributedText(text);
        if (paragraphRuns)
        {
            attributed.SetParagraph(Prose.Length + 1, Prose.Length, new ParagraphAttributes { MarginLeft = 1 });
        }

        var layout = Layout(new BaselineGrid(Step, FirstLineOnly: true));
        layout.LineBreaking = breaking;
        layout.Frames = [new RectangleF(0, 0, 300, 600)];
        layout.ProcessText(attributed, FontSize, new Size(300, 600), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var second = Enumerable.Range(0, layout.LineCount).First(i => layout.GetLine(i).Start > Prose.Length);

        Assert.That(OnGrid(Baselines(layout)[second]), Is.True, string.Join(", ", Baselines(layout)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ADropCap_SpansTheLinesAsTheGridSetsThem(bool inFrames)
    {
        var layout = Layout(new BaselineGrid(Step));
        layout.DropCap = new DropCap(3);
        if (inFrames)
        {
            layout.Frames = [new RectangleF(0, 0, 300, 600)];
        }

        layout.ProcessText(Prose, FontSize, new Size(300, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var cap = layout.GetTextData().Single(glyph => glyph.PositionInString == 0);
        var font = layout.Font;
        var capTop = layout.GetLine(0).Baseline - font.CapsHeight * FontSize / font.UnitsPerEm;

        Assert.Multiple(() =>
        {
            Assert.That(cap.FontSize, Is.GreaterThan(FontSize * 2.5), "the cap is made");
            Assert.That(cap.Rect.Top, Is.EqualTo(capTop).Within(1.5), "its top at the cap height of the first line");
            Assert.That(cap.Rect.Bottom, Is.EqualTo(layout.GetLine(2).Baseline).Within(1.5));
        });
    }

    [TestCase(VerticalTextAlignment.Center)]
    [TestCase(VerticalTextAlignment.Bottom)]
    public void ATextTallerThanItsArea_MovesNoFurtherThanAsked(VerticalTextAlignment alignment)
    {
        var plain = Lay(Prose, null, height: 50, alignment: alignment).GetLine(0).Top;
        var gridded = Lay(Prose, new BaselineGrid(Step), height: 50, alignment: alignment).GetLine(0).Top;

        Assert.That(gridded, Is.GreaterThanOrEqualTo(plain - 0.5));
    }

    [Test]
    public void AFrameTooShortForTheGriddedFirstLine_PassesItOn()
    {
        var layout = Layout(new BaselineGrid(40));
        layout.Frames = [new RectangleF(0, 41, 300, 22), new RectangleF(320, 0, 300, 200)];
        layout.ProcessText(Prose, FontSize, new Size(620, 200), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var first = layout.GetTextData()[0];

        Assert.That(first.Rect.X, Is.GreaterThanOrEqualTo(320), "set in the second frame");
    }

    [Test]
    public void AGrid_IsFinite()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BaselineGrid(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BaselineGrid(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BaselineGrid(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BaselineGrid(double.Epsilon));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BaselineGrid(Step, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BaselineGrid(Step) with { Offset = double.NegativeInfinity });
        });
    }

    [Test]
    public void VerticalText_IsLeftAsItIs()
    {
        BaselineGrid[] grids = [null, new BaselineGrid(Step)];
        var glyphs = grids.Select(grid =>
        {
            var layout = Layout(grid, WritingMode.VerticalRightToLeft);
            layout.ProcessText("縦書きの文章です。", FontSize, new Size(double.NaN, 200), TextWrapping.WrapByWords,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout.GetTextData().Select(glyph => glyph.Rect).ToArray();
        }).ToArray();

        Assert.That(glyphs[1], Is.EqualTo(glyphs[0]));
    }
}
