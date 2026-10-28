using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Text flowing through frames - columns side by side, the rest overset - and around exclusions.</summary>
[TestFixture]
public class TextFrameTests
{
    private const double FontSize = 20;

    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone " +
        "in her face. Close by the king's castle lay a great dark forest, and under an old lime tree in the forest " +
        "was a well, and when the day was very warm, the king's child went out into the forest and sat down by the " +
        "side of the cool fountain.";

    private static readonly Lazy<Typeface> SourceSans = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf")));

    private static double LineHeight { get; } = Layout("x", null).GetLine(0).Height;

    private static TextLayout Layout(string text, RectangleF[] frames, RectangleF[] exclusions = null,
        LineBreaking breaking = LineBreaking.Greedy, HorizontalTextAlignment alignment = HorizontalTextAlignment.Left,
        DropCap dropCap = null, TextDirection direction = TextDirection.Auto)
    {
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null,
            Frames = frames,
            Exclusions = exclusions,
            LineBreaking = breaking,
            DropCap = dropCap,
            Direction = direction,
        };
        layout.ProcessText(text, FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            alignment, VerticalTextAlignment.Top);
        return layout;
    }

    private static RectangleF[] Columns(int count, float width, float gap, int lines) =>
        Enumerable.Range(0, count)
            .Select(c => new RectangleF(c * (width + gap), 0, width, (float)(lines * LineHeight + 1)))
            .ToArray();

    private static GlyphWordData[] Ink(TextLayout layout, int line) =>
        layout.GetTextData().Where(g => g.LineIndex == line && g.Symbol != ' ').ToArray();

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void TextFlowsFromColumnToColumn(LineBreaking breaking)
    {
        var columns = Columns(3, 200, 20, 4);
        var layout = Layout(Prose, columns, breaking: breaking);

        for (var line = 0; line < layout.LineCount; line++)
        {
            var column = columns[line / 4];
            var ink = Ink(layout, line);
            Assert.That(ink.Min(g => g.Rect.Left), Is.GreaterThanOrEqualTo(column.Left - 1), $"line {line}");
            Assert.That(ink.Max(g => g.Rect.Right), Is.LessThanOrEqualTo(column.Right + 0.5), $"line {line}");
            Assert.That(layout.GetLine(line).Top, Is.EqualTo((line % 4) * LineHeight).Within(1e-3), $"line {line}");
        }
    }

    // The underline is drawn from the line's baseline; a line moved into a later column by a part of a pixel kept its
    // letters rounded where it was set.
    [Test]
    public void InEveryColumn_TheLettersStandOnTheirLinesBaseline()
    {
        var layout = Layout(Prose, Columns(3, 200, 20, 4));

        foreach (var glyph in layout.GetTextData().Where(g => g.Symbol != ' ' && g.PositionInString >= 0))
        {
            var baseline = glyph.Rect.Bottom + glyph.Glyph.BoundingRectangle.Y * glyph.FontSize / glyph.Font.UnitsPerEm;
            Assert.That(baseline, Is.EqualTo(layout.GetLine(glyph.LineIndex).Baseline).Within(0.01),
                $"line {glyph.LineIndex}");
        }
    }

    [Test]
    public void WhatTheFramesHaveNoRoomFor_IsOverset()
    {
        var layout = Layout(Prose, Columns(2, 200, 20, 3));
        var laid = layout.GetTextData().Where(g => g.PositionInString >= 0).Max(g => g.PositionInString);

        Assert.That(layout.LineCount, Is.EqualTo(6));
        Assert.That(layout.OversetIndex, Is.GreaterThan(laid));
        Assert.That(layout.OversetIndex, Is.LessThan(Prose.Length));
        Assert.That(Prose[layout.OversetIndex - 1], Is.EqualTo(' '), "the overset starts with a word");
    }

    [Test]
    public void TextThatFits_IsNotOverset()
    {
        Assert.That(Layout(Prose, Columns(3, 300, 20, 10)).OversetIndex, Is.EqualTo(Prose.Length));
    }

    [Test]
    public void LinesBesideAnExclusionOnTheLeft_StartPastIt()
    {
        var frame = new RectangleF(0, 0, 300, (float)(10 * LineHeight));
        var exclusion = new RectangleF(0, 0, 100, (float)(2 * LineHeight - 1));
        var layout = Layout(Prose, [frame], [exclusion]);

        Assert.That(Ink(layout, 0).Min(g => g.Rect.Left), Is.GreaterThanOrEqualTo(100));
        Assert.That(Ink(layout, 1).Min(g => g.Rect.Left), Is.GreaterThanOrEqualTo(100));
        Assert.That(Ink(layout, 2).Min(g => g.Rect.Left), Is.LessThan(5), "the third line is past it");
    }

    [Test]
    public void LinesBesideAnExclusionOnTheRight_EndBeforeIt()
    {
        var frame = new RectangleF(0, 0, 300, (float)(10 * LineHeight));
        var exclusion = new RectangleF(200, 0, 100, (float)(3 * LineHeight - 1));
        var layout = Layout(Prose, [frame], [exclusion]);

        for (var line = 0; line < 3; line++)
        {
            Assert.That(Ink(layout, line).Max(g => g.Rect.Right), Is.LessThanOrEqualTo(200.5), $"line {line}");
        }
    }

    [Test]
    public void ARowAnExclusionCovers_IsSkipped()
    {
        var frame = new RectangleF(0, 0, 300, (float)(10 * LineHeight));
        var exclusion = new RectangleF(0, (float)(LineHeight + 1), 300, (float)LineHeight - 2);
        var layout = Layout(Prose, [frame], [exclusion]);

        Assert.That(layout.GetLine(1).Top, Is.EqualTo(exclusion.Bottom).Within(1e-3), "right under it");
    }

    // Word's "square" wrapping on both sides: a row runs on past the exclusion before the next row starts.
    [TestCase(TextDirection.LeftToRight)]
    [TestCase(TextDirection.RightToLeft)]
    public void AnExclusionInTheMiddle_HasTextOnBothSides_ARowReadInTheTextsOrder(TextDirection direction)
    {
        var frame = new RectangleF(0, 0, 400, (float)(10 * LineHeight));
        var exclusion = new RectangleF(160, 0, 80, (float)(2 * LineHeight - 1));
        var layout = Layout(Prose, [frame], [exclusion], direction: direction);
        var first = direction == TextDirection.LeftToRight ? (Left: 0f, Right: 160f) : (Left: 240f, Right: 400f);
        var second = direction == TextDirection.LeftToRight ? (Left: 240f, Right: 400f) : (Left: 0f, Right: 160f);

        Assert.Multiple(() =>
        {
            for (var line = 0; line < 4; line++)
            {
                var side = line % 2 == 0 ? first : second;
                Assert.That(Ink(layout, line).Min(g => g.Rect.Left), Is.GreaterThanOrEqualTo(side.Left - 0.5),
                    $"line {line}");
                Assert.That(Ink(layout, line).Max(g => g.Rect.Right), Is.LessThanOrEqualTo(side.Right + 0.5),
                    $"line {line}");
                Assert.That(layout.GetLine(line).Top, Is.EqualTo(line / 2 * LineHeight).Within(1e-3), $"line {line}");
            }

            Assert.That(layout.GetLine(4).Top, Is.EqualTo(2 * LineHeight).Within(1e-3), "past it, a row of one line");
            Assert.That(layout.GetLine(5).Top, Is.EqualTo(3 * LineHeight).Within(1e-3));
        });
    }

    [Test]
    public void AParagraphBesideAnExclusion_GoesOnBesideIt_UnlessItKeepsRoomAbove()
    {
        var typeface = SourceSans.Value;
        AttributedText Two(double spaceBefore)
        {
            var text = new AttributedText("one" + (char)0x2029 + "two words more");
            text.SetParagraph(0, 4, new ParagraphAttributes());
            text.SetParagraph(4, 14, new ParagraphAttributes { SpaceBefore = spaceBefore });
            return text;
        }

        TextLayout Lay(double spaceBefore)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null,
                Frames = [new RectangleF(0, 0, 400, (float)(10 * LineHeight))],
                Exclusions = [new RectangleF(160, 0, 80, (float)(3 * LineHeight))],
            };
            layout.ProcessText(Two(spaceBefore), FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout;
        }

        var beside = Lay(0);
        var spaced = Lay(10);

        Assert.Multiple(() =>
        {
            Assert.That(beside.GetLine(1).Top, Is.EqualTo(beside.GetLine(0).Top).Within(1e-3));
            Assert.That(Ink(beside, 1).Min(g => g.Rect.Left), Is.GreaterThanOrEqualTo(239.5));
            Assert.That(spaced.GetLine(1).Top, Is.EqualTo(LineHeight + 10).Within(1e-3), "a row of its own");
            Assert.That(Ink(spaced, 1).Max(g => g.Rect.Right), Is.LessThanOrEqualTo(160.5));
        });
    }

    [Test]
    public void ADropCapBesideAnExclusionInTheMiddle_IsStillSet_ItsLinesOneARow()
    {
        var frame = new RectangleF(0, 0, 400, (float)(10 * LineHeight));
        var exclusion = new RectangleF(200, 0, 80, (float)(5 * LineHeight));
        var layout = Layout(Prose, [frame], [exclusion], dropCap: new DropCap(3));

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetTextData().Any(g => g.FontSize > FontSize), "the drop cap");
            Assert.That(layout.GetLine(1).Top, Is.GreaterThan(layout.GetLine(0).Top));
            Assert.That(layout.GetLine(2).Top, Is.GreaterThan(layout.GetLine(1).Top));
        });
    }

    [Test]
    public void AnEmptyLineBesideAnExclusion_HasItsCaretInItsOwnStretch()
    {
        var frame = new RectangleF(0, 0, 400, (float)(10 * LineHeight));
        var layout = Layout("Hello\n\nWorld", [frame], [new RectangleF(160, 0, 80, (float)(2 * LineHeight - 1))]);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetLine(1).Top, Is.EqualTo(layout.GetLine(0).Top).Within(1e-3), "beside the first");
            Assert.That(layout.GetCaretRect(new CaretPosition(6), 1).X, Is.GreaterThanOrEqualTo(239.5));
        });
    }

    [Test]
    public void ParagraphsReadingEachTheirOwnWay_TakeTheirStretchesInTheirOrder()
    {
        var frame = new RectangleF(0, 0, 400, (float)(10 * LineHeight));
        var exclusion = new RectangleF(160, 0, 80, (float)(4 * LineHeight - 1));
        var hebrew = string.Join(' ', Enumerable.Repeat("עברית", 30));
        var layout = Layout("one\n" + hebrew, [frame], [exclusion]);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetLine(1).Top, Is.EqualTo(LineHeight).Within(1e-3), "the other way, a row of its own");
            Assert.That(Ink(layout, 1).Min(g => g.Rect.Left), Is.GreaterThanOrEqualTo(239.5), "the right first");
            Assert.That(layout.GetLine(2).Top, Is.EqualTo(LineHeight).Within(1e-3));
            Assert.That(Ink(layout, 2).Max(g => g.Rect.Right), Is.LessThanOrEqualTo(160.5));
        });
    }

    [Test]
    public void AnArrowPastTheEmptyLastLine_OfAFullFrame_GoesNowhereAmiss()
    {
        var layout = Layout("one two\n", [new RectangleF(0, 0, 400, (float)LineHeight + 1)],
            [new RectangleF(300, 0, 20, 5)]);

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => layout.GetLineBeyond(0, 10, true));
            Assert.DoesNotThrow(() => layout.GetLineBeyond(layout.LineCount - 1, 10, false));
        });
    }

    [Test]
    public void AStretchTooNarrowForText_IsLeftEmpty()
    {
        var frame = new RectangleF(0, 0, 400, (float)(10 * LineHeight));
        var exclusion = new RectangleF(20, 0, 200, (float)(LineHeight - 1));
        var layout = Layout(Prose, [frame], [exclusion]);

        Assert.That(Ink(layout, 0).Min(g => g.Rect.Left), Is.GreaterThanOrEqualTo(220));
        Assert.That(layout.GetLine(1).Top, Is.EqualTo(LineHeight).Within(1e-3));
    }

    [Test]
    public void AClickOrAnArrowBesideAnExclusion_FindsTheLineOnItsSide()
    {
        var frame = new RectangleF(0, 0, 400, (float)(10 * LineHeight));
        var exclusion = new RectangleF(160, 0, 80, (float)(2 * LineHeight - 1));
        var layout = Layout(Prose, [frame], [exclusion]);
        var right = layout.GetLine(1);

        var hit = layout.HitTest(300, right.Top + right.Height / 2);

        Assert.Multiple(() =>
        {
            Assert.That(hit.CaretIndex, Is.InRange(right.Start, right.End));
            Assert.That(layout.GetLineBeyond(0, 50, true), Is.EqualTo(2), "down from the left, the left below");
            Assert.That(layout.GetLineBeyond(1, 300, true), Is.EqualTo(3), "down from the right, the right");
            Assert.That(layout.GetLineBeyond(3, 300, true), Is.EqualTo(4), "past it, the one line of the row below");
            Assert.That(layout.GetLineBeyond(4, 50, false), Is.EqualTo(2));
            Assert.That(layout.GetLineBeyond(0, 50, false), Is.EqualTo(-1));
        });
    }

    [Test]
    public void JustifiedLines_FillTheirColumn()
    {
        var columns = Columns(2, 200, 20, 6);
        var layout = Layout(Prose, columns, breaking: LineBreaking.Paragraph, alignment: HorizontalTextAlignment.Justify);

        Assert.That(Ink(layout, 1).Max(g => g.Rect.Right), Is.EqualTo(columns[0].Right).Within(0.5));
        Assert.That(Ink(layout, 7).Max(g => g.Rect.Right), Is.EqualTo(columns[1].Right).Within(0.5));
    }

    [Test]
    public void AClickInTheSecondColumn_HitsItsLine()
    {
        var columns = Columns(2, 200, 20, 3);
        var layout = Layout(Prose, columns);
        var line = layout.GetLine(4);
        var hit = layout.HitTest(columns[1].Left + 30, line.Top + line.Height / 2);

        Assert.That(hit.CaretIndex, Is.InRange(line.Start, line.End));
    }

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void AWordBrokenOnTheLastRow_KeepsWithinTheFrame(LineBreaking breaking)
    {
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null,
            Frames = [new RectangleF(0, 0, 150, (float)LineHeight + 1)],
            LineBreaking = breaking,
            Hyphens = Hyphens.Auto,
            Language = "en-US",
        };
        layout.ProcessText("Internationalization of extraordinarily sophisticated applications", FontSize,
            new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Left,
            VerticalTextAlignment.Top);

        Assert.That(layout.GetTextData().Where(g => g.Symbol != ' ').Max(g => g.Rect.Right), Is.LessThanOrEqualTo(150.5));
        Assert.That(layout.OversetIndex, Is.LessThan(66));
    }

    [Test]
    public void ACaretPastTheLastLine_StopsAtTheOverset()
    {
        var columns = Columns(2, 200, 20, 3);
        var layout = Layout(Prose, columns);
        var last = layout.GetLine(layout.LineCount - 1);
        var hit = layout.HitTest(columns[1].Right - 2, last.Top + last.Height / 2);

        Assert.That(hit.CaretIndex, Is.LessThanOrEqualTo(layout.OversetIndex));
        Assert.That(last.End, Is.LessThanOrEqualTo(layout.OversetIndex));
    }

    [Test]
    public void FramesWithNoRoom_LeaveNoLinesOfAnEarlierLayout()
    {
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, Frames = Columns(1, 300, 0, 10) };
        layout.ProcessText(Prose, FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        layout.Frames = [new RectangleF(0, 0, 20, 100)];
        layout.ProcessText("short", FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.OversetIndex, Is.Zero);
        Assert.That(layout.HitTest(250, 60).CaretIndex, Is.LessThanOrEqualTo(5));
    }

    [Test]
    public void ADropCapTallerThanItsColumn_IsNotSet()
    {
        var layout = Layout(Prose, Columns(3, 200, 20, 2), dropCap: new DropCap(3));

        Assert.That(layout.GetTextData().All(g => g.FontSize <= FontSize));
    }

    [Test]
    public void ANewlineEndingTheLastRow_PutsNoLineOverIt()
    {
        var layout = Layout("one\n", [new RectangleF(0, 0, 200, (float)LineHeight + 1)]);

        Assert.That(layout.OversetIndex, Is.EqualTo(4));
        Assert.That(layout.GetLine(layout.LineCount - 1).Top, Is.GreaterThanOrEqualTo(layout.GetLine(0).Top + LineHeight - 1e-3));
    }

    [Test]
    public void TheLastJustifiedLineBeforeTheOverset_IsStretched()
    {
        var columns = Columns(1, 200, 0, 3);
        var layout = Layout(Prose, columns, breaking: LineBreaking.Paragraph, alignment: HorizontalTextAlignment.Justify);

        Assert.That(Ink(layout, 2).Max(g => g.Rect.Right), Is.EqualTo(200).Within(0.5));
    }

    // A line raised by larger text takes the room it needs: the line after it stands below it, and one it raises past
    // the bottom of its frame goes on to the next frame.
    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void ALineRaisedByLargerText_TakesItsRoomInTheFrame(LineBreaking breaking)
    {
        var text = new AttributedText(Prose);
        var big = Prose.IndexOf("king", StringComparison.Ordinal);
        text.Apply(big, 4, new TextAttributes { FontSize = 48 });
        var frames = Columns(2, 220, 20, 4);
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, Frames = frames, LineBreaking = breaking };
        layout.ProcessText(text, FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        for (var i = 0; i < layout.LineCount; i++)
        {
            var line = layout.GetLine(i);
            var frame = frames[layout.GetTextData().Where(g => g.LineIndex == i).Select(g => g.Rect.X)
                .DefaultIfEmpty(0).Min() >= frames[1].X - 0.5 ? 1 : 0];
            if (line.Start >= layout.OversetIndex)
            {
                continue;
            }

            Assert.That(line.Top + line.Height, Is.LessThanOrEqualTo(frame.Bottom + 0.5), $"line {i} past its frame's bottom");
            if (i + 1 < layout.LineCount && layout.GetLine(i + 1).Top > line.Top)
            {
                Assert.That(layout.GetLine(i + 1).Top, Is.GreaterThanOrEqualTo(line.Top + line.Height - 0.5),
                    $"line {i + 1} over line {i}");
            }
        }
    }

    // A line raised by larger text found its room at its paragraph's line height: its lower part reached into an
    // exclusion just under that room.
    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void ALineRaisedByLargerText_KeepsOutOfAnExclusion(LineBreaking breaking)
    {
        var text = new AttributedText(Prose);
        text.Apply(Prose.IndexOf("times", StringComparison.Ordinal), 5, new TextAttributes { FontSize = 48 });
        var exclusion = new RectangleF(0, (float)LineHeight + 4, 120, 60);
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, Frames = [new RectangleF(0, 0, 300, 600)], Exclusions = [exclusion], LineBreaking = breaking,
        };
        layout.ProcessText(text, FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.Multiple(() =>
        {
            for (var i = 0; i < layout.LineCount; i++)
            {
                var line = layout.GetLine(i);
                var reaches = line.Top < exclusion.Bottom && line.Top + line.Height > exclusion.Top
                              && Ink(layout, i).Any(glyph => glyph.Rect.Left < exclusion.Right);
                Assert.That(reaches, Is.False, $"line {i} reaches into the exclusion");
                if (i > 0)
                {
                    var before = layout.GetLine(i - 1);
                    Assert.That(line.Top, Is.GreaterThanOrEqualTo(before.Top + before.Height - 0.5), $"line {i} over line {i - 1}");
                }
            }

            Assert.That(Enumerable.Range(0, 2).Any(i => Ink(layout, i).Any(glyph => glyph.FontSize > FontSize)), Is.True,
                "the larger text is on one of the first two lines, beside the exclusion");
        });
    }

    [Test]
    public void ADropCap_StandsAtTheStartOfTheFirstFrame()
    {
        var frames = new[] { new RectangleF(50, 10, 250, (float)(10 * LineHeight)) };
        var layout = Layout(Prose, frames, dropCap: new DropCap(3));
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);

        Assert.That(cap.PenX, Is.EqualTo(50).Within(1e-3));
        Assert.That(Ink(layout, 0).Where(g => g.PositionInString > 0).Min(g => g.Rect.Left), Is.GreaterThan(cap.Rect.Right));
    }

    [Test]
    public void ExclusionsOutsideAFrame_DoNotSendShortTextOnToTheNext()
    {
        var outside = Enumerable.Range(0, 30).Select(i => new RectangleF(1000, 0, 10, 10 + 3 * i));
        var layout = Layout("Hi", [new RectangleF(0, 0, 300, 400), new RectangleF(400, 0, 300, 400)],
            [new RectangleF(-10, 0, 320, 100), .. outside]);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetLineFrame(0), Is.Zero);
            Assert.That(layout.GetLine(0).Top, Is.EqualTo(100).Within(1e-3));
        });
    }

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void UnderAnExclusionAcrossTheWholeFrame_TheTextGoesOnRightWhereItEnds(LineBreaking breaking)
    {
        var bottom = (float)(1.4 * LineHeight);
        var layout = Layout(Prose, [new RectangleF(0, 0, 300, 1000)], [new RectangleF(-50, 0, 400, bottom)], breaking);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetLine(0).Top, Is.EqualTo(bottom).Within(1e-3), "not a whole row on");
            Assert.That(layout.GetLine(1).Top - layout.GetLine(0).Top, Is.EqualTo(LineHeight).Within(1e-3));
        });
    }
}
