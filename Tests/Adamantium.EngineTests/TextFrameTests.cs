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
        DropCap dropCap = null)
    {
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null,
            Frames = frames,
            Exclusions = exclusions,
            LineBreaking = breaking,
            DropCap = dropCap,
        };
        layout.ProcessText(text, FontSize, new Size(double.NaN, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
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

        Assert.That(layout.GetLine(1).Top, Is.EqualTo(2 * LineHeight).Within(1e-3));
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
            new Size(double.NaN, double.NaN), TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Left,
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
        layout.ProcessText(Prose, FontSize, new Size(double.NaN, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        layout.Frames = [new RectangleF(0, 0, 20, 100)];
        layout.ProcessText("short", FontSize, new Size(double.NaN, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
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

    [Test]
    public void ADropCap_StandsAtTheStartOfTheFirstFrame()
    {
        var frames = new[] { new RectangleF(50, 10, 250, (float)(10 * LineHeight)) };
        var layout = Layout(Prose, frames, dropCap: new DropCap(3));
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);

        Assert.That(cap.PenX, Is.EqualTo(50).Within(1e-3));
        Assert.That(Ink(layout, 0).Where(g => g.PositionInString > 0).Min(g => g.Rect.Left), Is.GreaterThan(cap.Rect.Right));
    }
}
