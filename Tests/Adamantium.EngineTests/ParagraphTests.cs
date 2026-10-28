using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>PARAGRAPHS of their own in one layout, as a page has them: each with its alignment, its distance from the
/// edges and its first line's, the room around it and the height of its lines. U+2029 ends one and starts the next.</summary>
[TestFixture]
public class ParagraphTests
{
    private const double FontSize = 20;
    private const char Separator = (char)0x2029;

    private const string Prose = "Once upon a time there lived a king whose daughters were all beautiful, but the " +
                                 "youngest was so beautiful that the sun itself was astonished whenever it shone.";

    private static string FontPath(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", name);

    private static TextLayout Lay(AttributedText text, double width = 300, Action<TextLayout> setUp = null,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left)
    {
        var typeface = Typeface.LoadFont(FontPath("SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        setUp?.Invoke(layout);
        layout.ProcessText(text, FontSize, new Size<double>(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            alignment, VerticalTextAlignment.Top);
        return layout;
    }

    // Two paragraphs, the second given its own attributes (and the first plain ones, unless asked).
    private static AttributedText Two(string first, string second, ParagraphAttributes secondAttributes,
        ParagraphAttributes firstAttributes = null)
    {
        var text = new AttributedText(first + Separator + second);
        text.SetParagraph(0, first.Length + 1, firstAttributes ?? new ParagraphAttributes());
        text.SetParagraph(first.Length + 1, second.Length, secondAttributes);
        return text;
    }

    private static double[] LineStarts(TextLayout layout) => Enumerable.Range(0, layout.LineCount)
        .Select(line => layout.GetTextData().Where(glyph => glyph.LineIndex == line && glyph.Symbol is not (' ' or '\n'))
            .Select(glyph => (double)glyph.Rect.X).DefaultIfEmpty(double.NaN).Min())
        .ToArray();

    private static double[] LineEnds(TextLayout layout) => Enumerable.Range(0, layout.LineCount)
        .Select(line => layout.GetTextData().Where(glyph => glyph.LineIndex == line && glyph.Symbol is not (' ' or '\n'))
            .Select(glyph => (double)glyph.Rect.Right).DefaultIfEmpty(double.NaN).Max())
        .ToArray();

    [Test]
    public void TheSeparatorEndsALine()
    {
        var layout = Lay(new AttributedText("one" + Separator + "two"));

        Assert.That(layout.LineCount, Is.EqualTo(2));
    }

    [Test]
    public void AMarginMovesItsOwnParagraphOnly()
    {
        var layout = Lay(Two("plain", "indented", new ParagraphAttributes { MarginLeft = 30 }));
        var starts = LineStarts(layout);

        Assert.That(starts[1] - starts[0], Is.EqualTo(30).Within(1));
    }

    [Test]
    public void TheFirstLineIndentIsTheFirstLinesOnly()
    {
        var text = new AttributedText(Prose);
        text.SetParagraph(0, Prose.Length, new ParagraphAttributes { TextIndent = 25 });
        var starts = LineStarts(Lay(text, 200));

        Assume.That(starts.Length, Is.GreaterThan(2));
        Assert.Multiple(() =>
        {
            Assert.That(starts[0] - starts[1], Is.EqualTo(25).Within(1));
            Assert.That(starts[2], Is.EqualTo(starts[1]).Within(1));
        });
    }

    // A right-to-left paragraph's lines start at the right, so its first line is indented from there; the margins
    // keep their sides.
    [Test]
    public void ARightToLeftParagraphIndentsItsFirstLineFromTheRight()
    {
        var hebrew = string.Join(" ", Enumerable.Repeat("שלום עולם", 12));
        var text = new AttributedText(hebrew);
        text.SetParagraph(0, hebrew.Length, new ParagraphAttributes { TextIndent = 25, MarginLeft = 10 });
        var typeface = Typeface.LoadFont(FontPath("NotoSansHebrew-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(text, FontSize, new Size<double>(200, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var ends = LineEnds(layout);

        Assume.That(ends.Length, Is.GreaterThan(2));
        Assert.Multiple(() =>
        {
            Assert.That(ends[1], Is.EqualTo(200).Within(1), "the lines start at the right edge");
            Assert.That(ends[1] - ends[0], Is.EqualTo(25).Within(1), "the first further in");
            Assert.That(LineStarts(layout).Min(), Is.GreaterThanOrEqualTo(10 - 1), "the left margin stays left");
        });
    }

    // A negative first-line indent hangs the first line out over the margin - a list's marker, a bibliography.
    [Test]
    public void ANegativeIndentHangsTheFirstLine()
    {
        var text = new AttributedText(Prose);
        text.SetParagraph(0, Prose.Length, new ParagraphAttributes { MarginLeft = 30, TextIndent = -30 });
        var starts = LineStarts(Lay(text, 200));

        Assert.Multiple(() =>
        {
            Assert.That(starts[0], Is.LessThan(2));
            Assert.That(starts[1] - starts[0], Is.EqualTo(30).Within(1));
        });
    }

    [Test]
    public void TheRightMarginNarrowsWhereItsLinesBreak()
    {
        var text = new AttributedText(Prose);
        text.SetParagraph(0, Prose.Length, new ParagraphAttributes { MarginRight = 100 });
        var narrowed = Lay(text, 300);
        var plain = Lay(new AttributedText(Prose), 300);

        Assert.Multiple(() =>
        {
            Assert.That(narrowed.LineCount, Is.GreaterThan(plain.LineCount));
            Assert.That(LineEnds(narrowed).Max(), Is.LessThanOrEqualTo(200 + 1));
        });
    }

    [Test]
    public void EachParagraphHasItsOwnAlignment()
    {
        var layout = Lay(Two("right", "left", new ParagraphAttributes(),
            new ParagraphAttributes { Alignment = HorizontalTextAlignment.Right }));
        var ends = LineEnds(layout);
        var starts = LineStarts(layout);

        Assert.Multiple(() =>
        {
            Assert.That(ends[0], Is.EqualTo(300).Within(1), "the first stands at the right edge");
            Assert.That(starts[1], Is.LessThan(2), "the second at the left, as the layout says");
        });
    }

    [Test]
    public void AParagraphCentersBetweenItsOwnMargins()
    {
        var text = new AttributedText("middle");
        text.SetParagraph(0, 6, new ParagraphAttributes
        {
            Alignment = HorizontalTextAlignment.Center, MarginLeft = 100
        });
        var layout = Lay(text, 300);
        var middle = (LineStarts(layout)[0] + LineEnds(layout)[0]) / 2;

        Assert.That(middle, Is.EqualTo(200).Within(1.5));
    }

    // Between two paragraphs only the larger of the room below the first and above the second is kept, as on a page.
    [Test]
    public void TheRoomBetweenTwoParagraphsIsTheLargerOfTheirs()
    {
        var plain = Lay(new AttributedText("one" + Separator + "two"));
        var spaced = Lay(Two("one", "two", new ParagraphAttributes { SpaceBefore = 30 },
            new ParagraphAttributes { SpaceAfter = 10, SpaceBefore = 50 }));

        Assert.Multiple(() =>
        {
            Assert.That(spaced.GetLine(0).Top, Is.EqualTo(plain.GetLine(0).Top).Within(1e-6), "none above the first");
            Assert.That(spaced.GetLine(1).Top - plain.GetLine(1).Top, Is.EqualTo(30).Within(1e-6));
            Assert.That(spaced.GetTextData().First(glyph => glyph.LineIndex == 1).Rect.Y
                        - plain.GetTextData().First(glyph => glyph.LineIndex == 1).Rect.Y, Is.EqualTo(30).Within(1));
        });
    }

    [Test]
    public void AParagraphsLineHeightIsItsOwn()
    {
        var plain = Lay(new AttributedText("one" + Separator + "two"));
        var tall = Lay(Two("one", "two", new ParagraphAttributes { LineHeight = 60 }));

        Assert.Multiple(() =>
        {
            Assert.That(tall.GetLine(0).Height, Is.EqualTo(plain.GetLine(0).Height).Within(1e-6));
            Assert.That(tall.GetLine(1).Height, Is.EqualTo(60).Within(1e-6));
        });
    }

    // The paragraph-wide line breaker measures each paragraph against its own width.
    [Test]
    public void ParagraphLineBreakingHonorsTheMargins()
    {
        var text = new AttributedText(Prose);
        text.SetParagraph(0, Prose.Length, new ParagraphAttributes { MarginLeft = 40, MarginRight = 60 });
        var layout = Lay(text, 300, set => set.LineBreaking = LineBreaking.Paragraph);

        Assert.Multiple(() =>
        {
            Assert.That(LineStarts(layout).Min(), Is.GreaterThanOrEqualTo(40 - 1));
            Assert.That(LineEnds(layout).Max(), Is.LessThanOrEqualTo(240 + 1));
        });
    }

    [Test]
    public void OneParagraphJustifiesWhileTheNextDoesNot()
    {
        var layout = Lay(Two(Prose, Prose, new ParagraphAttributes(),
            new ParagraphAttributes { Alignment = HorizontalTextAlignment.Justify }), 300);
        var firstOfSecond = layout.GetTextData().First(glyph => glyph.PositionInString > Prose.Length).LineIndex;
        var ends = LineEnds(layout);

        Assert.Multiple(() =>
        {
            Assert.That(ends[0], Is.EqualTo(300).Within(1), "a line of the justified paragraph reaches the edge");
            Assert.That(ends[firstOfSecond], Is.LessThan(299), "a line of the next is ragged");
        });
    }

    // The paragraph-wide line breaker gives each paragraph's first line its indent, and only that line.
    [Test]
    public void ParagraphLineBreakingIndentsEachParagraphsFirstLineOnly()
    {
        var layout = Lay(Two(Prose, Prose, new ParagraphAttributes { TextIndent = 60 },
            new ParagraphAttributes { TextIndent = 120 }), 300, set => set.LineBreaking = LineBreaking.Paragraph);
        var starts = LineStarts(layout);
        var ends = LineEnds(layout);
        var firstOfSecond = layout.GetTextData().First(glyph => glyph.PositionInString > Prose.Length).LineIndex;

        Assert.Multiple(() =>
        {
            Assert.That(starts[0] - starts[1], Is.EqualTo(120).Within(1));
            Assert.That(starts[firstOfSecond] - starts[firstOfSecond + 1], Is.EqualTo(60).Within(1));
            for (var line = 0; line < ends.Length; line++)
            {
                if (line != firstOfSecond - 1 && line != ends.Length - 1)
                {
                    Assert.That(ends[line], Is.GreaterThan(220), $"line {line} is full");
                }

                if (line != 0 && line != firstOfSecond)
                {
                    Assert.That(starts[line], Is.LessThan(2), $"line {line} is not indented");
                }
            }
        });
    }

    private static readonly RectangleF[] OneFrame = [new RectangleF(0, 0, 200, 600)];

    [Test]
    public void InAFrameTheRoomBetweenParagraphsAndTheirLineHeightsHold()
    {
        var spaced = Lay(Two("one", Prose, new ParagraphAttributes { SpaceBefore = 30, LineHeight = 60 }), 200,
            set => set.Frames = OneFrame);
        var plain = Lay(new AttributedText("one" + Separator + Prose), 200, set => set.Frames = OneFrame);

        Assert.Multiple(() =>
        {
            Assert.That(spaced.GetLine(1).Top - plain.GetLine(1).Top, Is.EqualTo(30).Within(1e-6));
            Assert.That(spaced.GetLine(2).Top - spaced.GetLine(1).Top, Is.EqualTo(60).Within(1e-6));
            Assert.That(spaced.GetTextData().Where(glyph => glyph.LineIndex == 2 && glyph.Symbol != ' ')
                .Min(glyph => glyph.Rect.Top), Is.GreaterThan(spaced.GetLine(2).Top - 1));
        });
    }

    // As across a page break, the room above a paragraph is not kept at the top of the next frame.
    [Test]
    public void TheRoomAboveAParagraphIsNotKeptAtTheTopOfAFrame()
    {
        var line = Lay(new AttributedText("x")).GetLine(0).Height;
        RectangleF[] frames = [new RectangleF(0, 0, 100, (float)line + 1), new RectangleF(200, 0, 100, (float)line + 1)];
        var layout = Lay(Two("one", "two", new ParagraphAttributes { SpaceBefore = 30 }), 300, set => set.Frames = frames);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetLine(1).Top, Is.EqualTo(0).Within(1e-6));
            Assert.That(LineStarts(layout)[1], Is.GreaterThanOrEqualTo(200));
        });
    }

    // A paragraph's line height is its lines' least height, as the layout's is: larger text still grows its line.
    [Test]
    public void LargerTextGrowsALineOfAParagraphWithItsOwnHeight()
    {
        var text = new AttributedText("small" + Separator + "BIG");
        text.Apply(6, 3, new TextAttributes { FontSize = 60 });
        text.SetParagraph(6, 3, new ParagraphAttributes { LineHeight = 20 });
        var layout = Lay(text);
        var big = layout.GetTextData().Where(glyph => glyph.LineIndex == 1).Min(glyph => glyph.Rect.Top);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetLine(1).Height, Is.GreaterThan(60));
            Assert.That(big, Is.GreaterThanOrEqualTo(layout.GetLine(0).Top + layout.GetLine(0).Height - 1e-3));
        });
    }

    [TestCase(30, double.NaN)]
    [TestCase(0, 60)]
    public void TrimmingKeepsALineAfterTheRoomAboveItOrAnOwnHeightOutside(double spaceBefore, double lineHeight)
    {
        var text = Two("one", "two", new ParagraphAttributes { SpaceBefore = spaceBefore },
            new ParagraphAttributes { LineHeight = lineHeight });
        var typeface = Typeface.LoadFont(FontPath("SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(text, FontSize, new Size<double>(300, 50), TextWrapping.WrapByWords, TextTrimming.CharEllipses,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetTextData().Where(glyph => glyph.Symbol is not (' ' or '\n')).Max(glyph => glyph.Rect.Bottom),
            Is.LessThanOrEqualTo(50));
    }

    [Test]
    public void AnUnboundedLayoutIsAsWideAsItsWidestLineWithItsOwnMargin()
    {
        const string wide = "a rather long line of text";
        var layout = Lay(Two(wide, "short", new ParagraphAttributes { MarginRight = 80 }), double.NaN);
        var plain = Lay(new AttributedText(wide), double.NaN);

        Assert.That(layout.CalculatedLayoutSize.Width, Is.EqualTo(plain.CalculatedLayoutSize.Width).Within(1));
    }

    [Test]
    public void ADropCapStandsAtItsParagraphsMargin_InPlaceOfTheFirstLinesIndent()
    {
        var text = new AttributedText(Prose);
        text.SetParagraph(0, Prose.Length, new ParagraphAttributes { MarginLeft = 50, TextIndent = 25 });
        var layout = Lay(text, 300, set => set.DropCap = new DropCap(3));
        var cap = layout.GetTextData().Where(glyph => glyph.PositionInString == 0).ToArray();
        var besideCap = Enumerable.Range(0, 2).Select(line => layout.GetTextData()
            .Where(glyph => glyph.LineIndex == line && glyph.PositionInString > 0 && glyph.Symbol != ' ')
            .Min(glyph => (double)glyph.Rect.X)).ToArray();

        Assume.That(cap, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(cap.Min(glyph => glyph.Rect.X), Is.EqualTo(50).Within(8));
            Assert.That(besideCap[0], Is.EqualTo(besideCap[1]).Within(3), "no indent beside the cap");
            Assert.That(besideCap[0], Is.GreaterThan(cap.Max(glyph => glyph.Rect.Right)));
        });
    }

    [Test]
    public void WrappingBySymbolsKeepsTheMargins()
    {
        var text = new AttributedText(Prose);
        text.SetParagraph(0, Prose.Length, new ParagraphAttributes { MarginLeft = 40, MarginRight = 40 });
        var typeface = Typeface.LoadFont(FontPath("SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(text, FontSize, new Size<double>(200, double.NaN), TextWrapping.WrapBySymbols, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assume.That(layout.LineCount, Is.GreaterThan(2));
        Assert.Multiple(() =>
        {
            Assert.That(LineStarts(layout).Min(), Is.GreaterThanOrEqualTo(40 - 1));
            Assert.That(LineEnds(layout).Max(), Is.LessThanOrEqualTo(160 + 1));
        });
    }

    // An empty paragraph after the closing separator is a line with its own room above it.
    [Test]
    public void AnEmptyLastParagraphKeepsItsOwnAttributes()
    {
        var text = new AttributedText("A" + Separator);
        text.SetParagraph(0, 2, new ParagraphAttributes());
        text.SetParagraph(2, 0, new ParagraphAttributes { SpaceBefore = 40 });
        var layout = Lay(text);
        var plain = Lay(new AttributedText("A" + Separator));

        Assert.Multiple(() =>
        {
            Assert.That(text.ParagraphAt(2)?.Attributes.SpaceBefore, Is.EqualTo(40));
            Assert.That(layout.LineCount, Is.EqualTo(2));
            Assert.That(layout.GetLine(1).Top - plain.GetLine(1).Top, Is.EqualTo(40).Within(1e-6));
        });
    }

    [TestCase(HorizontalTextAlignment.Left, 40)]
    [TestCase(HorizontalTextAlignment.Right, 300 - 20)]
    [TestCase(HorizontalTextAlignment.Center, (40 + 280) / 2)]
    public void TheCaretOnAnEmptyLastParagraphStandsAsItSays(HorizontalTextAlignment alignment, double expected)
    {
        var text = new AttributedText("A" + Separator);
        text.SetParagraph(2, 0, new ParagraphAttributes
        {
            Alignment = alignment, MarginLeft = 30, MarginRight = 20, TextIndent = 10,
        });
        var layout = Lay(text);

        Assert.That(layout.GetCaretPoint(new CaretPosition(2, false)).X, Is.EqualTo(expected).Within(0.5));
    }

    // A slot found ahead before the room above a paragraph was known is found again: the line does not land on the one
    // before, and what no longer fits is overset.
    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void ALineThatNoLongerFitsAFrameAfterTheRoomAboveItIsOverset(LineBreaking breaking)
    {
        var layout = Lay(Two("one", "two", new ParagraphAttributes { SpaceBefore = 20 }), 300, set =>
        {
            set.Frames = [new RectangleF(0, 0, 300, 60)];
            set.LineBreaking = breaking;
        });

        Assert.That(layout.OversetIndex, Is.EqualTo(4));
    }

    [Test]
    public void ADropCapInAFrameStaysInIt_WhenTheNextParagraphKeepsRoomAboveIt()
    {
        var layout = Lay(Two("Hello world", "two more words", new ParagraphAttributes { SpaceBefore = 25 }), 300, set =>
        {
            set.Frames = [new RectangleF(0, 0, 300, 200)];
            set.DropCap = new DropCap(2);
        });
        var tops = Enumerable.Range(0, layout.LineCount).Select(line => layout.GetLine(line).Top).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetTextData().Min(glyph => glyph.Rect.Top), Is.GreaterThanOrEqualTo(-0.5));
            Assert.That(tops, Is.Ordered.Ascending);
            Assert.That(tops.Distinct().Count(), Is.EqualTo(tops.Length));
        });
    }

    // The paragraph-wide line breaker measures a frame's lines with the room above each paragraph: past an exclusion
    // they are full again.
    [Test]
    public void ParagraphLineBreakingInAFrameMeasuresLinesWhereTheyEndUp()
    {
        var text = new AttributedText(Prose + Separator + Prose + Separator + Prose);
        for (var start = 0; start < text.Text.Length; start += Prose.Length + 1)
        {
            text.SetParagraph(start, Math.Min(Prose.Length + 1, text.Text.Length - start),
                new ParagraphAttributes { SpaceBefore = 100 });
        }

        var layout = Lay(text, 300, set =>
        {
            set.Frames = [new RectangleF(0, 0, 300, 1000)];
            set.Exclusions = [new RectangleF(150, 120, 150, 200)];
            set.LineBreaking = LineBreaking.Paragraph;
        });
        var ends = LineEnds(layout);
        var lastOfParagraph = Enumerable.Range(0, layout.LineCount)
            .Where(line => line == layout.LineCount - 1 || LineGapAbove(layout, line + 1) > 50).ToHashSet();

        Assert.Multiple(() =>
        {
            for (var line = 0; line < layout.LineCount; line++)
            {
                if (layout.GetLine(line).Top > 330 && !lastOfParagraph.Contains(line))
                {
                    Assert.That(ends[line], Is.GreaterThan(220), $"line {line} below the exclusion is full");
                }
            }
        });
    }

    private static double LineGapAbove(TextLayout layout, int line) =>
        layout.GetLine(line).Top - layout.GetLine(line - 1).Top - layout.GetLine(line - 1).Height;

    // A line break at the end of the last paragraph goes on in it: no room below it, its margin kept.
    [TestCase('\n')]
    [TestCase((char)0x2028)]
    public void ALineBreakEndingTheLastParagraphGoesOnInIt(char lineBreak)
    {
        var text = new AttributedText("abc" + lineBreak);
        text.SetParagraph(0, 4, new ParagraphAttributes { MarginLeft = 40, SpaceAfter = 50 });
        var layout = Lay(text);
        var plain = Lay(new AttributedText("abc" + lineBreak));

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetLine(1).Top, Is.EqualTo(plain.GetLine(1).Top).Within(1e-6));
            Assert.That(layout.CalculatedLayoutSize.Height, Is.EqualTo(plain.CalculatedLayoutSize.Height).Within(1e-6));
            Assert.That(layout.GetCaretPoint(new CaretPosition(4, false)).X, Is.EqualTo(40).Within(0.5));
        });
    }

    [TestCase(40, double.NaN)]
    [TestCase(0, 60)]
    public void ADropCapReachingIntoTheNextParagraphStaysAtTheTop(double spaceBefore, double lineHeight)
    {
        void Cap(TextLayout layout) => layout.DropCap = new DropCap(3);
        var spaced = Lay(Two("Hi", Prose, new ParagraphAttributes { SpaceBefore = spaceBefore, LineHeight = lineHeight }),
            300, Cap);
        var plain = Lay(Two("Hi", Prose, new ParagraphAttributes()), 300, Cap);

        float CapTop(TextLayout layout) => layout.GetTextData().Where(glyph => glyph.PositionInString == 0)
            .Min(glyph => glyph.Rect.Top);

        Assert.That(CapTop(spaced), Is.EqualTo(CapTop(plain)).Within(0.5));
    }

    [Test]
    public void ANegativeSpacingNeverLiftsALineAboveTheOneBefore()
    {
        var text = new AttributedText(Prose);
        text.SetParagraph(0, Prose.Length, new ParagraphAttributes { LineHeight = 8 });
        var layout = Lay(text, 200, set => set.LineSpacing = -20);
        var tops = Enumerable.Range(0, layout.LineCount).Select(line => layout.GetLine(line).Top).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(tops, Is.Ordered.Ascending);
            Assert.That(tops.Distinct().Count(), Is.EqualTo(tops.Length));
            Assert.That(layout.CalculatedLayoutSize.Height, Is.GreaterThan(0));
        });
    }

    [Test]
    public void ValuesThatAreNoNumbersCountAsNone()
    {
        var text = Two("one", Prose, new ParagraphAttributes
        {
            MarginLeft = double.NaN, MarginRight = double.PositiveInfinity, TextIndent = double.NaN,
            SpaceBefore = double.NaN, SpaceAfter = double.NegativeInfinity,
        });
        var layout = Lay(text, 200);
        var plain = Lay(new AttributedText("one" + Separator + Prose), 200);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetTextData().All(glyph => float.IsFinite(glyph.Rect.X) && float.IsFinite(glyph.Rect.Y)));
            Assert.That(LineStarts(layout), Is.EqualTo(LineStarts(plain)).Within(1e-3));
            Assert.That(layout.GetLine(layout.LineCount - 1).Top, Is.EqualTo(plain.GetLine(plain.LineCount - 1).Top));
        });
    }

    // A line break inside a paragraph (U+2028) is not a new paragraph: no first-line indent, no room around it.
    [Test]
    public void ALineBreakInsideAParagraphIsNotANewOne()
    {
        var line = (char)0x2028;
        var text = new AttributedText("first" + line + "second");
        text.SetParagraph(0, text.Text.Length, new ParagraphAttributes { TextIndent = 25, SpaceBefore = 30 });
        var layout = Lay(text);
        var plain = Lay(new AttributedText("first" + line + "second"));

        Assert.Multiple(() =>
        {
            Assert.That(LineStarts(layout)[1], Is.EqualTo(LineStarts(plain)[1]).Within(1));
            Assert.That(layout.GetLine(1).Top, Is.EqualTo(plain.GetLine(1).Top).Within(1e-6));
        });
    }
}
