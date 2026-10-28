using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>An empty line between two hard breaks starts after the first of them: the line before kept both breaks, and
/// a caret at the start of the empty line stood on the line before.</summary>
[TestFixture]
public class HardBreakLineTests
{
    private static readonly string LineSeparator = ((char)0x2028).ToString();
    private static readonly string ParagraphSeparator = ((char)0x2029).ToString();

    private static TextLayout Layout(string text, bool newlineCarets, double width = 500,
        TextWrapping wrapping = TextWrapping.WrapByWords, TextTrimming trimming = TextTrimming.None,
        Action<TextLayout> setUp = null)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, EmitNewlineCarets = newlineCarets };
        setUp?.Invoke(layout);
        layout.ProcessText(text, 16, new Size<double>(width, double.NaN), wrapping, trimming, HorizontalTextAlignment.Left,
            VerticalTextAlignment.Top);
        return layout;
    }

    private static string Lines(TextLayout layout) => string.Join(" ",
        Enumerable.Range(0, layout.LineCount).Select(i => $"{layout.GetLine(i).Start}..{layout.GetLine(i).End}"));

    private static int LineOfCaret(TextLayout layout, int index) =>
        layout.GetCaretPoint(new CaretPosition(index)).LineIndex;

    [TestCase("\n", false)]
    [TestCase("\n", true)]
    [TestCase("ls", false)]
    [TestCase("ps", false)]
    [TestCase("ps", true)]
    public void AnEmptyLine_StartsAfterTheBreakBeforeIt(string separator, bool newlineCarets)
    {
        var at = separator switch { "ls" => LineSeparator, "ps" => ParagraphSeparator, _ => separator };
        var layout = Layout("ab" + at + at + "cd", newlineCarets);

        Assert.That(Lines(layout), Is.EqualTo("0..3 3..4 4..6"));
        Assert.That(LineOfCaret(layout, 3), Is.EqualTo(1), "the start of the empty line");
        Assert.That(LineOfCaret(layout, 2), Is.EqualTo(0), "before the first break");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyLinesInARow_EachStartAfterItsBreak(bool newlineCarets)
    {
        var layout = Layout("a\n\n\nb", newlineCarets);

        Assert.That(Lines(layout), Is.EqualTo("0..2 2..3 3..4 4..5"));
        Assert.That(LineOfCaret(layout, 2), Is.EqualTo(1));
        Assert.That(LineOfCaret(layout, 3), Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyLinesFirst_EachStartAfterItsBreak(bool newlineCarets)
    {
        var layout = Layout("\n\nab", newlineCarets);

        Assert.That(Lines(layout), Is.EqualTo("0..1 1..2 2..4"));
        Assert.That(LineOfCaret(layout, 0), Is.EqualTo(0));
        Assert.That(LineOfCaret(layout, 1), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ACarriageReturnAndLineFeed_BreakOnce(bool newlineCarets)
    {
        var layout = Layout("ab\r\n\r\ncd", newlineCarets);

        Assert.That(Lines(layout), Is.EqualTo("0..4 4..6 6..8"));
        Assert.That(LineOfCaret(layout, 4), Is.EqualTo(1));
    }

    [TestCase("\n", false)]
    [TestCase("\n", true)]
    [TestCase("ls", false)]
    [TestCase("ps", false)]
    public void ABreakAtTheEnd_LeavesAnEmptyLastLine(string separator, bool newlineCarets)
    {
        var at = separator switch { "ls" => LineSeparator, "ps" => ParagraphSeparator, _ => separator };
        var layout = Layout("ab" + at, newlineCarets);

        Assert.That(Lines(layout), Is.EqualTo("0..3 3..3"));
        Assert.That(LineOfCaret(layout, 3), Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OnlyBreaks_MakeAnEmptyLineEach(bool newlineCarets)
    {
        var layout = Layout("\n\n", newlineCarets);

        Assert.That(Lines(layout), Is.EqualTo("0..1 1..2 2..2"));
        Assert.That(LineOfCaret(layout, 1), Is.EqualTo(1));
    }

    // An ellipsis ends the text it shows: breaks after it are not laid out and make no lines.
    [TestCase("hello world again\n\nfoo bar", TextWrapping.WrapByWords, TextTrimming.CharEllipses, 3)]
    [TestCase("hello world again\n\n\nfoo bar", TextWrapping.WrapByWords, TextTrimming.WordEllipses, 2)]
    [TestCase("abcdefghijk\n\nxyz", TextWrapping.NoWrap, TextTrimming.CharEllipses, 0)]
    [TestCase("abcdefghijk\nxyz\n", TextWrapping.NoWrap, TextTrimming.CharEllipses, 0)]
    public void BreaksAfterAnEllipsis_MakeNoLines(string text, TextWrapping wrapping, TextTrimming trimming, int maxLines)
    {
        var layout = Layout(text, false, 40, wrapping, trimming, l => l.MaxLines = maxLines);
        var height = layout.CalculatedLayoutSize.Height;

        Assert.That(layout.LineCount, Is.LessThanOrEqualTo(maxLines > 0 ? maxLines : 1));
        foreach (var rect in layout.GetRangeRects(0, text.Length))
        {
            Assert.That(rect.Y + rect.Height, Is.LessThanOrEqualTo(height + 1e-3), "a selection under the text");
        }
    }

    [Test]
    public void WhatIsLeftOutAfterAnEmptyLastLine_StandsAtItsStart()
    {
        var layout = Layout("a\n\n\nb", false, setUp: l => l.MaxLines = 2);
        var stops = layout.GetCaretStops();

        Assert.That(stops[3].LineIndex, Is.EqualTo(1));
        Assert.That(stops[3].X, Is.EqualTo(0).Within(1e-6));
        Assert.That(layout.GetLine(1).Width, Is.EqualTo(0).Within(1e-6));
    }

    [Test]
    public void AnEmptyLineInAFrame_StandsInIt()
    {
        var layout = Layout("ab\n\ncd\n\nef", false, setUp: l => l.Frames =
            [new RectangleF(0, 0, 100, 20), new RectangleF(120, 0, 100, 200)]);
        var point = layout.GetCaretPoint(new CaretPosition(3));

        Assert.That(point.LineIndex, Is.EqualTo(1));
        Assert.That(point.X, Is.EqualTo(120).Within(1e-6), "the second frame's left edge");
    }

    [Test]
    public void AnEmptyRightToLeftLineInAFrame_StandsAtThatFramesRightEdge()
    {
        var layout = Layout("ab\n\ncd\nef", false, setUp: l =>
        {
            l.Direction = TextDirection.RightToLeft;
            l.Frames = [new RectangleF(0, 0, 100, 50), new RectangleF(120, 0, 100, 200)];
        });
        var stop = layout.GetCaretStops()[3];

        Assert.That(stop.LineIndex, Is.EqualTo(1));
        Assert.That(stop.X, Is.EqualTo(100).Within(1e-3), "at the right edge of the first frame, where its line is");
    }

    [Test]
    public void AnEmptyRightToLeftLineInAnEmptyFrame_StandsAtItsRightEdge()
    {
        var layout = Layout("ab\n\n", false, setUp: l =>
        {
            l.Direction = TextDirection.RightToLeft;
            l.Frames = [new RectangleF(0, 0, 100, 20), new RectangleF(120, 0, 100, 200)];
        });
        var stops = layout.GetCaretStops();

        Assert.That(stops[4].X, Is.EqualTo(220).Within(1e-3));
    }

    [Test]
    public void TheEndOfALine_StandsAfterItsLastLetter_NotWithinIt()
    {
        var layout = Layout("ab\n\ncd", false);
        var stops = layout.GetCaretStops();
        var b = layout.GetTextData().Single(glyph => glyph.PositionInString == 1);

        Assert.That(stops[2].X, Is.EqualTo(b.PenX + b.Advance).Within(1e-6), "the breaks took no share of b");
        Assert.That(stops[3].X, Is.EqualTo(0).Within(1e-6), "the empty line starts at its left edge");
    }
}
