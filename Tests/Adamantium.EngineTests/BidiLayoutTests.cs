using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Text of both directions laid out: each line in visual order by the Unicode Bidirectional Algorithm, the
/// caret before a right-to-left character on its right edge, selections and clicks where the characters stand.</summary>
[TestFixture]
public class BidiLayoutTests
{
    private const string Hebrew = "שלום";

    private static TextLayout Layout(string text, TextDirection direction = TextDirection.Auto)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, Direction = direction };
        layout.ProcessText(text, 20, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    // A paragraph running right to left, 400 wide.
    private static TextLayout RightToLeft(string text, ParagraphAttributes paragraph, IReadOnlyList<TabStop> stops = null)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, Direction = TextDirection.RightToLeft, TabStops = stops,
        };
        layout.ProcessText(new AttributedText(text, null).SetParagraph(0, text.Length, paragraph), 20,
            new Size<double>(400, double.NaN), TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Left,
            VerticalTextAlignment.Top);
        return layout;
    }

    // An empty paragraph an editor shows by a space: its caret where a line of text would start - inside the margin,
    // at the right, a justified one too - or in the middle.
    [TestCase(HorizontalTextAlignment.Left, 370, 370)]
    [TestCase(HorizontalTextAlignment.Justify, 370, 370)]
    [TestCase(HorizontalTextAlignment.Center, 180, 200)]
    public void ALineOfSpacesAlone_StandsAsALineOfTextWould_InARightToLeftParagraph(HorizontalTextAlignment alignment,
        double least, double most)
    {
        var layout = RightToLeft(" ", new ParagraphAttributes { MarginRight = 30, Alignment = alignment });

        Assert.That(layout.GetCaretPoint(new CaretPosition(0)).X, Is.InRange(least - 0.5, most + 0.5));
    }

    // Measured from the edge its lines start at, the right one, whatever the margins and the first line's indent.
    [TestCase(0, 0, 0)]
    [TestCase(40, 0, 0)]
    [TestCase(0, 40, 0)]
    [TestCase(0, 0, 30)]
    public void ATabStop_IsMeasuredFromTheRight_InAParagraphRunningRightToLeft(double left, double right, double indent)
    {
        var layout = RightToLeft("א\tב", new ParagraphAttributes { MarginLeft = left, MarginRight = right, TextIndent = indent },
            [new TabStop(100)]);

        // The caret before ב stands on its right edge, where the tab ends; the line starts inside its margin and indent.
        var fromStart = layout.GetCaretPoint(new CaretPosition(0)).X - layout.GetCaretPoint(new CaretPosition(2)).X;
        Assert.That(fromStart + right + indent, Is.EqualTo(100).Within(0.5));
    }

    // Past an exclusion at the right the stop is still measured from the frame's edge, as at the left.
    [Test]
    public void ATabStop_PastAnExclusion_IsMeasuredFromTheFramesRightEdge()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, Direction = TextDirection.RightToLeft, TabStops = [new TabStop(100)],
            Exclusions = [new RectangleF(350, 0, 50, 1000)],
        };
        layout.ProcessText("א\tב", 20, new Size<double>(400, 200), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(400 - layout.GetCaretPoint(new CaretPosition(2)).X, Is.EqualTo(100).Within(1));
    }

    private static int[] VisualClusters(TextLayout layout) =>
        layout.GetTextData().OrderBy(g => g.PenX).Select(g => g.PositionInString).ToArray();

    private static CaretPosition ToPosition(TextHit hit) => new(hit.CaretIndex, hit.IsTrailing);

    private static double Point(TextLayout layout, TextHit hit) => layout.GetCaretPoint(ToPosition(hit)).X;

    [Test]
    public void AHebrewWordInALeftToRightLine_RunsRightToLeft()
    {
        var layout = Layout($"abc {Hebrew} def");

        Assert.That(VisualClusters(layout), Is.EqualTo(new[] { 0, 1, 2, 3, 7, 6, 5, 4, 8, 9, 10, 11 }));
    }

    // Auto takes the paragraph's direction from its first strong character: Hebrew, so the line runs right to left. The
    // number after the Latin word takes the word's direction (W7), and the two keep their order, left to right.
    [Test]
    public void ARightToLeftParagraph_KeepsLatinAndNumbersLeftToRight()
    {
        var layout = Layout($"{Hebrew} abc 123");

        Assert.That(VisualClusters(layout), Is.EqualTo(new[] { 5, 6, 7, 8, 9, 10, 11, 4, 3, 2, 1, 0 }));
    }

    // A paragraph asked to run right to left keeps a Latin phrase left to right, and puts its closing period, at the
    // paragraph's own level, on the phrase's left.
    [Test]
    public void AParagraphAskedToRunRightToLeft_PutsTheClosingPeriodOnTheLeft()
    {
        var layout = Layout("abc, def.", TextDirection.RightToLeft);

        Assert.That(VisualClusters(layout), Is.EqualTo(new[] { 8, 0, 1, 2, 3, 4, 5, 6, 7 }));
    }

    // A paragraph separator that does not break the line still starts a paragraph of its own: the Hebrew after it
    // runs right to left.
    [Test]
    public void AParagraphSeparatorInsideALine_StartsAParagraphOfItsOwn()
    {
        var layout = Layout("ab" + (char)0x2029 + Hebrew);

        var hebrew = VisualClusters(layout).Where(c => c > 2).ToArray();

        Assert.That(hebrew, Is.EqualTo(new[] { 6, 5, 4, 3 }));
    }

    [Test]
    public void ALoneSurrogate_IsLaidOut()
    {
        Assert.DoesNotThrow(() => Layout("x" + (char)0xD83D + "y"));
        Assert.DoesNotThrow(() => Layout(Hebrew + (char)0xDE00));
    }

    // Left is where a line starts: the right edge for a right-to-left paragraph, the left for the other.
    [Test]
    public void LeftAlignment_PutsARightToLeftLineOnTheRightEdge()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText($"{Hebrew}\nabc", 20, new Size<double>(300, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var glyphs = layout.GetTextData();

        Assert.Multiple(() =>
        {
            Assert.That(glyphs.Where(g => g.LineIndex == 0).Max(g => g.Rect.Right), Is.EqualTo(300).Within(0.5));
            Assert.That(glyphs.Where(g => g.LineIndex == 1).Min(g => g.PenX), Is.EqualTo(0).Within(1e-6));
            Assert.That(layout.IsRightToLeftParagraph(0));
            Assert.That(layout.IsRightToLeftParagraph(Hebrew.Length + 1), Is.False);
        });
    }

    [Test]
    public void TheCaretBeforeARightToLeftCharacter_StandsOnItsRightEdge()
    {
        var layout = Layout(Hebrew);
        var stops = layout.GetCaretStops();
        var first = layout.GetTextData().Single(g => g.PositionInString == 0);

        Assert.Multiple(() =>
        {
            Assert.That(stops[0].IsRightToLeft);
            Assert.That(stops[0].X, Is.EqualTo(first.PenX + first.Advance).Within(1e-6));
            Assert.That(stops.Take(Hebrew.Length).Select(s => s.X), Is.Ordered.Descending);
            Assert.That(stops[Hebrew.Length].X, Is.EqualTo(stops[Hebrew.Length - 1].Left).Within(1e-6),
                "after the last character, on its left edge");
        });
    }

    [Test]
    public void TheCaretAfterARightToLeftCharacter_StandsOnItsLeftEdge()
    {
        var layout = Layout($"abc {Hebrew}");
        var stops = layout.GetCaretStops();

        Assert.Multiple(() =>
        {
            Assert.That(stops[4].After, Is.EqualTo(stops[4].Left));
            Assert.That(stops[0].After, Is.EqualTo(stops[1].X));
        });
    }

    // The end of the text stands where its paragraph ends, not after its last character in the middle of the line.
    [Test]
    public void TheCaretAtTheEnd_StandsOnTheEdgeTheParagraphEndsOn()
    {
        var text = $"abc {Hebrew}";
        var layout = Layout(text);
        var rightToLeft = Layout($"{Hebrew} abc");
        var stops = rightToLeft.GetCaretStops();

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetCaretStops()[text.Length].X, Is.EqualTo(layout.GetLine(0).Width).Within(1e-6));
            Assert.That(stops[text.Length].X, Is.EqualTo(stops[Hebrew.Length + 1].Left).Within(1e-6),
                "on the left of the Latin word");
        });
    }

    // Spaces a right-to-left paragraph starts with take their room at its start, inside the frame, however its lines are
    // aligned, and with an ellipsis at their end.
    [TestCase(HorizontalTextAlignment.Left)]
    [TestCase(HorizontalTextAlignment.Justify)]
    public void SpacesStartingARightToLeftParagraph_StandAtItsRightInsideTheFrame(HorizontalTextAlignment alignment)
    {
        var layout = RightToLeft("  " + Hebrew, new ParagraphAttributes { Alignment = alignment });

        Assert.That(layout.GetCaretPoint(new CaretPosition(0)).X, Is.EqualTo(400).Within(0.5));
    }

    [Test]
    public void SpacesStartingARightToLeftLine_CutWithAnEllipsis_StandInsideTheFrame()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, Direction = TextDirection.RightToLeft };
        layout.ProcessText("  " + string.Join(" ", Enumerable.Repeat(Hebrew, 10)), 20, new Size<double>(200, double.NaN),
            TextWrapping.NoWrap, TextTrimming.CharEllipses, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetCaretPoint(new CaretPosition(0)).X, Is.EqualTo(200).Within(0.5));
    }

    // A line of spaces alone stands at the right of a paragraph running right to left by the layout's direction too.
    [Test]
    public void ALineOfSpacesAlone_StandsAtTheRight_InATextRunningRightToLeft()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, Direction = TextDirection.RightToLeft };
        var text = $"{Hebrew}\n   \n{Hebrew}";
        layout.ProcessText(text, 20, new Size<double>(400, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetCaretPoint(new CaretPosition(Hebrew.Length + 1)).X, Is.EqualTo(400).Within(0.5));
    }

    // A line feed ends a right-to-left paragraph: the caret on the empty line after it starts on the right.
    [Test]
    public void TheEmptyLastLineOfARightToLeftParagraph_StartsOnTheRight()
    {
        var layout = Layout($"{Hebrew}\n", TextDirection.RightToLeft);
        var stops = layout.GetCaretStops();

        Assert.That(stops[Hebrew.Length + 1].X, Is.EqualTo(stops.Take(Hebrew.Length).Max(s => s.Right)).Within(1e-6));
    }

    // White space where a line wraps stands at the paragraph's level (L1): its caret box is where its glyph is drawn.
    [Test]
    public void EveryCaretBox_StandsWhereItsGlyphIsDrawn()
    {
        var text = $"abc {Hebrew} {Hebrew} {Hebrew} {Hebrew} def";
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(text, 20, new Size<double>(120, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var stops = layout.GetCaretStops();

        Assert.Multiple(() =>
        {
            foreach (var glyph in layout.GetTextData().Where(g => g.PositionInString >= 0 && g.Advance > 0))
            {
                var stop = stops[glyph.PositionInString];
                Assert.That(stop.Left, Is.EqualTo(glyph.PenX).Within(1e-3), $"left of {glyph.PositionInString}");
                Assert.That(stop.Right, Is.EqualTo(glyph.PenX + glyph.Advance).Within(1e-3),
                    $"right of {glyph.PositionInString}");
            }
        });
    }

    // Selecting across the change of direction covers two pieces of the line: the Hebrew on the right of the space
    // and the start of the Latin word on its left.
    [Test]
    public void ASelectionAcrossDirections_CoversEachPieceOfTheLine()
    {
        var layout = Layout($"{Hebrew} abc", TextDirection.RightToLeft);

        var rects = layout.GetRangeRects(0, 6);

        Assert.That(rects, Has.Count.EqualTo(2));
    }

    [Test]
    public void AClickOnTheRightHalfOfAHebrewLetter_PutsTheCaretBeforeIt()
    {
        var layout = Layout(Hebrew);
        var first = layout.GetTextData().Single(g => g.PositionInString == 0);
        var line = layout.GetLine(0);

        var right = layout.HitTest(first.PenX + first.Advance * 0.9, line.Top + line.Height / 2);
        var left = layout.HitTest(first.PenX + first.Advance * 0.1, line.Top + line.Height / 2);

        Assert.Multiple(() =>
        {
            Assert.That(right.CaretIndex, Is.EqualTo(0));
            Assert.That(left.CaretIndex, Is.EqualTo(1));
        });
    }

    // A line ending in Hebrew ends on screen with the word's first letter: a click past it stands on the line's edge.
    [TestCase(TextDirection.LeftToRight)]
    [TestCase(TextDirection.RightToLeft)]
    public void AClickBesideALine_PutsTheCaretOnTheLineEdge(TextDirection direction)
    {
        var layout = Layout($"abc {Hebrew}", direction);
        var line = layout.GetLine(0);
        var y = line.Top + line.Height / 2;
        var stops = layout.GetCaretStops();
        var left = stops.Take(stops.Length - 1).Min(s => s.Left);
        var right = stops.Take(stops.Length - 1).Max(s => s.Right);

        var pastRight = layout.HitTest(right + 50, y);
        var pastLeft = layout.HitTest(left - 50, y);

        Assert.Multiple(() =>
        {
            Assert.That(Point(layout, pastRight), Is.EqualTo(right).Within(1e-6));
            Assert.That(Point(layout, pastLeft), Is.EqualTo(left).Within(1e-6));
        });
    }

    // Typing after End or a click past the line appends: the caret goes to the text's end, which stands on the edge the
    // paragraph ends on, not before the Hebrew word that ends the line on screen.
    [TestCase(TextDirection.LeftToRight)]
    [TestCase(TextDirection.RightToLeft)]
    public void TheLineEnd_IsTheEndOfTheText(TextDirection direction)
    {
        var text = $"abc {Hebrew}";
        var layout = Layout(text, direction);
        var line = layout.GetLine(0);
        var stops = layout.GetCaretStops();
        var endSide = direction == TextDirection.RightToLeft
            ? stops.Take(text.Length).Min(s => s.Left) - 50
            : stops.Take(text.Length).Max(s => s.Right) + 50;

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetLineEnd(0).Index, Is.EqualTo(text.Length));
            Assert.That(layout.HitTest(endSide, line.Top + line.Height / 2).CaretIndex, Is.EqualTo(text.Length));
        });
    }

    [TestCase(TextDirection.LeftToRight)]
    [TestCase(TextDirection.RightToLeft)]
    public void StepsToTheRight_CrossEveryLetterOnScreenFromLeftToRight(TextDirection direction)
    {
        var text = $"abc {Hebrew} def";
        var layout = Layout(text, direction);
        var line = layout.GetLine(0);
        var position = ToPosition(layout.HitTest(-50, line.Top + line.Height / 2));

        var xs = new List<double> { layout.GetCaretPoint(position).X };
        for (var step = 0; step < text.Length; step++)
        {
            position = layout.MoveVisually(position, toRight: true);
            xs.Add(layout.GetCaretPoint(position).X);
        }

        Assert.Multiple(() =>
        {
            Assert.That(xs, Is.Ordered.Ascending);
            Assert.That(xs.Distinct().Count(), Is.EqualTo(text.Length + 1), "one letter a step");
            Assert.That(layout.MoveVisually(position, toRight: true).Index, Is.EqualTo(position.Index), "the end stays");
        });
    }

    // Invisible marks take no width; the caret steps over them rather than stopping on them.
    [TestCase(0x200F, "cd")]
    [TestCase(0x200B, "cd")]
    [TestCase(0x200F, Hebrew)]
    public void StepsToTheRight_PassInvisibleMarks(int mark, string tail)
    {
        var text = "ab" + (char)mark + tail;
        var layout = Layout(text);
        var position = layout.GetLineStart(0);
        for (var step = 0; step < text.Length + 2; step++)
        {
            position = layout.MoveVisually(position, toRight: true);
        }

        Assert.That(layout.GetCaretPoint(position).X,
            Is.EqualTo(layout.GetCaretStops().Take(text.Length).Max(s => s.Right)).Within(1e-6));
    }

    // From the end of "abc" to the middle of the Hebrew word on screen: the space and the word's last two letters,
    // which the text keeps apart from the space by the word's first two.
    [Test]
    public void AVisualSelection_TakesTheLettersBetweenTwoPointsOnScreen()
    {
        var layout = Layout($"abc {Hebrew} def");
        var anchor = new CaretPosition(3, afterPrevious: true);
        var focus = new CaretPosition(6);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetVisualRanges(anchor, focus), Is.EqualTo(new[] { (3, 4), (6, 8) }));
            Assert.That(layout.GetVisualRanges(focus, anchor), Is.EqualTo(new[] { (3, 4), (6, 8) }), "either way");
            Assert.That(layout.GetVisualRanges(anchor, anchor), Is.Empty, "a point takes nothing");
        });
    }

    [Test]
    public void SelectingEverythingOnScreen_TakesBothCharactersOfACarriageReturnLineFeed()
    {
        var text = $"{Hebrew}\r\nabc";
        var layout = Layout(text);

        var ranges = layout.GetVisualRanges(layout.GetLineStart(0), layout.GetLineEnd(1));

        Assert.That(ranges, Is.EqualTo(new[] { (0, text.Length) }));
    }

    // Ctrl+arrows go by the screen, as the plain arrows do: each word step lands further that way, on a word's start.
    [TestCase(TextDirection.LeftToRight, true)]
    [TestCase(TextDirection.LeftToRight, false)]
    [TestCase(TextDirection.RightToLeft, true)]
    [TestCase(TextDirection.RightToLeft, false)]
    public void WordSteps_GoByTheScreen(TextDirection direction, bool toRight)
    {
        var text = $"abc {Hebrew} {Hebrew} def ghi";
        var layout = Layout(text, direction);
        var leftToRight = direction == TextDirection.LeftToRight;
        var leftEdge = leftToRight ? layout.GetLineStart(0) : layout.GetLineEnd(0);
        var rightEdge = leftToRight ? layout.GetLineEnd(0) : layout.GetLineStart(0);
        var position = toRight ? leftEdge : rightEdge;
        var x = layout.GetCaretPoint(position).X;
        var steps = 0;
        while (steps++ < 20)
        {
            var next = layout.MoveWordVisually(position, toRight);
            if (next.Index == position.Index && next.AfterPrevious == position.AfterPrevious)
            {
                break;
            }

            var nextX = layout.GetCaretPoint(next).X;
            Assert.That(toRight ? nextX > x + 0.5 : nextX < x - 0.5, Is.True,
                $"step {steps} went from {x} to {nextX} (index {next.Index})");
            var atEdge = layout.MoveVisually(next, toRight) is var beyond && beyond.Index == next.Index
                                                                         && beyond.AfterPrevious == next.AfterPrevious;
            Assert.That(atEdge || next.Index == text.Length || text[next.Index] != ' ', Is.True,
                $"step {steps} stopped in the spaces, at {next.Index}");
            position = next;
            x = nextX;
        }

        Assert.That(steps, Is.GreaterThanOrEqualTo(5), "it went word by word");
    }

    // Ctrl+Right and Ctrl+Left stop at the same places, numbers and runs of the other direction among them.
    [TestCase("שלום 123 456 עולם", TextDirection.RightToLeft, new[] { 0, 5, 9, 13 })]
    [TestCase("abc שלום עולם def", TextDirection.LeftToRight, new[] { 0, 4, 9, 14 })]
    public void WordSteps_StopAtTheSamePlaces_BothWays(string text, TextDirection direction, int[] words)
    {
        var layout = Layout(text, direction);
        var leftToRight = direction == TextDirection.LeftToRight;
        var leftEdge = leftToRight ? layout.GetLineStart(0) : layout.GetLineEnd(0);
        var rightEdge = leftToRight ? layout.GetLineEnd(0) : layout.GetLineStart(0);

        var rightward = Steps(layout, leftEdge, true);
        var leftward = Steps(layout, rightEdge, false);

        Assert.That(rightward.Select(x => Math.Round(x, 2)),
            Is.EqualTo(leftward.Select(x => Math.Round(x, 2)).Reverse().Skip(1).Append(Math.Round(layout.GetCaretPoint(rightEdge).X, 2))));
        var places = rightward.Prepend(layout.GetCaretPoint(leftEdge).X).ToArray();
        foreach (var word in words)
        {
            Assert.That(places.Any(x => Math.Abs(x - layout.GetCaretPoint(new CaretPosition(word)).X) < 0.01), Is.True,
                $"a stop at the word at {word}");
        }
    }

    // Across paragraphs running different ways the arrows go from line to line as the text's own direction reads, so
    // they get through it, word by word or letter by letter, and never come round again.
    [TestCase("abc def\nשלום עולם\nghi jkl", true)]
    [TestCase("abc def\nשלום עולם\nghi jkl", false)]
    [TestCase("שלום עולם\nabc def\nטוב מאוד", true)]
    [TestCase("שלום עולם\nabc def\nטוב מאוד", false)]
    public void TheArrows_GetThroughParagraphsOfBothDirections(string text, bool byWords)
    {
        var layout = Layout(text);
        var rightToLeft = layout.GetLine(0).Start == 0 && text[0] >= 0x0590 && text[0] <= 0x05FF;
        var onwards = !rightToLeft;
        var position = layout.GetLineStart(0);
        var seen = new HashSet<(int, bool)> { (position.Index, position.AfterPrevious) };
        for (var i = 0; i < 4 * text.Length; i++)
        {
            var next = byWords ? layout.MoveWordVisually(position, onwards) : layout.MoveVisually(position, onwards);
            if (next.Index == position.Index && next.AfterPrevious == position.AfterPrevious)
            {
                break;
            }

            Assert.That(seen.Add((next.Index, next.AfterPrevious)), Is.True, $"came round again to {next.Index}");
            position = next;
        }

        Assert.That(layout.GetCaretPoint(position).LineIndex, Is.EqualTo(layout.LineCount - 1), "through to the last line");
    }

    // A soft wrap is one stop: the caret goes from the last word of a line to the first of the next.
    [Test]
    public void WordSteps_StopOnceAtAWrap()
    {
        const string text = "aaaa bbbb cccc dddd eeee ffff";
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(text, 20, new Size<double>(100, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        Assume.That(layout.LineCount, Is.GreaterThan(1));
        var position = new CaretPosition(0);
        var indices = new List<int>();
        for (var i = 0; i < 20; i++)
        {
            var next = layout.MoveWordVisually(position, true);
            if (next.Index == position.Index && next.AfterPrevious == position.AfterPrevious)
            {
                break;
            }

            indices.Add(next.Index);
            position = next;
        }

        Assert.That(indices, Is.EqualTo(new[] { 5, 10, 15, 20, 25, text.Length }));
    }

    private static double[] Steps(TextLayout layout, CaretPosition from, bool toRight)
    {
        var xs = new List<double>();
        var position = from;
        for (var i = 0; i < 40; i++)
        {
            var next = layout.MoveWordVisually(position, toRight);
            if (next.Index == position.Index && next.AfterPrevious == position.AfterPrevious)
            {
                break;
            }

            xs.Add(layout.GetCaretPoint(next).X);
            position = next;
        }

        return xs.ToArray();
    }
}
