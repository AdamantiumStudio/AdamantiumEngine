using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Fonts.Shaping;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Vertical text (<see cref="WritingMode.VerticalRightToLeft"/>): lines run down and stack from the right,
/// ideographs and kana stand upright in their vertical forms, Latin lies turned, and one or two digits stand upright
/// across the line.</summary>
[TestFixture]
public class VerticalLayoutTests
{
    private const double FontSize = 20;

    private static readonly Lazy<Typeface> NotoSansCjk = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansCJK-Regular.ttc")));

    private static IFont Font => NotoSansCjk.Value.Fonts[0];

    private static double LineHeight => (Font.LineAscent + Font.LineDescent + Font.LineGap) * FontSize / Font.UnitsPerEm;

    [Test]
    public void Ideographs_StandUprightDownTheRightmostLine()
    {
        var glyphs = Lay("漢字かな", 200, 400).GetTextData();

        Assert.That(glyphs.All(glyph => glyph.Upright && !glyph.Sideways), Is.True);
        for (var i = 0; i < glyphs.Length; i++)
        {
            Assert.That(glyphs[i].PenX, Is.EqualTo(i * FontSize).Within(1e-6), "an em down the line each");
            Assert.That(UprightCenter(glyphs[i]), Is.EqualTo(200 - LineHeight / 2).Within(0.01),
                "centered across the rightmost line");
            Assert.That(glyphs[i].Rect.Top, Is.GreaterThan(i * FontSize - 0.5));
            Assert.That(glyphs[i].Rect.Bottom, Is.LessThan((i + 1) * FontSize + 0.5), "inside its em");
        }
    }

    [Test]
    public void Lines_StackFromRightToLeft()
    {
        var glyphs = Lay(string.Concat(Enumerable.Repeat("漢字", 20)), 1000, 100).GetTextData();

        Assert.That(glyphs.Max(glyph => glyph.LineIndex), Is.EqualTo(7), "five ideographs a line");
        foreach (var glyph in glyphs)
        {
            Assert.That(UprightCenter(glyph), Is.EqualTo(1000 - (glyph.LineIndex + 0.5) * LineHeight).Within(0.01));
            Assert.That(glyph.PenX + glyph.Advance, Is.LessThanOrEqualTo(100 + 1e-6), "no line longer than the area is high");
        }
    }

    [Test]
    public void Latin_LiesTurnedClockwise_ItsEmCenteredOnTheLine()
    {
        var glyphs = Lay("漢Abc", 200, 400).GetTextData();
        var scale = FontSize / Font.UnitsPerEm;

        Assert.That(glyphs[0].Upright, Is.True);
        foreach (var glyph in glyphs.Skip(1))
        {
            Assert.That(glyph.Sideways, Is.True, $"{glyph.Symbol} lies turned");
            var baseline = glyph.Rect.X - glyph.Glyph.BoundingRectangle.Y * scale;
            var emCenter = baseline + (Font.LineAscent - Font.LineDescent) / 2.0 * scale;
            Assert.That(emCenter, Is.EqualTo(200 - LineHeight / 2).Within(0.6), $"{glyph.Symbol} centered across the line");
            Assert.That(glyph.Rect.Top, Is.GreaterThanOrEqualTo(FontSize - 0.5), "below the ideograph");
        }

        Assert.That(glyphs[2].Rect.Top, Is.GreaterThan(glyphs[1].Rect.Bottom - 2), "read downward");
    }

    [Test]
    public void OneOrTwoDigits_StandUprightAcrossTheLine()
    {
        var glyphs = Lay("12月", 200, 400).GetTextData();
        var center = 200 - LineHeight / 2;

        Assert.That(glyphs.All(glyph => glyph.Upright), Is.True);
        Assert.That(glyphs[0].Advance + glyphs[1].Advance, Is.EqualTo(FontSize).Within(1e-6), "the digits take one em");
        Assert.That(glyphs[2].PenX, Is.EqualTo(FontSize).Within(1e-6), "the next character an em down");
        Assert.That(glyphs[1].Rect.Left, Is.GreaterThan(glyphs[0].Rect.Right - 1), "side by side");
        Assert.That((glyphs[0].Rect.Left + glyphs[1].Rect.Right) / 2, Is.EqualTo(center).Within(1.5));
        Assert.That(glyphs[1].Rect.Right - glyphs[0].Rect.Left, Is.LessThanOrEqualTo(FontSize), "no wider than an em");
        Assert.That(glyphs.Take(2).All(glyph => glyph.Rect.Top > -0.5 && glyph.Rect.Bottom < FontSize + 0.5), Is.True);
    }

    [Test]
    public void UprightDigits_WrapTogether()
    {
        var glyphs = Lay("漢12", 200, 30, wrapping: TextWrapping.WrapBySymbols).GetTextData();
        var digits = glyphs.Where(glyph => char.IsAsciiDigit(glyph.Symbol)).ToArray();

        Assert.That(digits.Select(glyph => glyph.LineIndex), Is.All.EqualTo(1), "both on the next line");
        Assert.That(digits.All(glyph => glyph.Rect.Top > -0.5 && glyph.Rect.Bottom < FontSize + 0.5), Is.True,
            "in the first em of the line");
    }

    [Test]
    public void UprightDigits_TrimmedBefore_LeaveTheEllipsisClear()
    {
        var glyphs = Lay("漢漢12漢漢", 200, 50, wrapping: TextWrapping.NoWrap, trimming: TextTrimming.CharEllipses)
            .GetTextData();
        var dots = glyphs.Where(glyph => glyph.PositionInString < 0).ToArray();

        Assert.That(dots, Has.Length.EqualTo(3));
        foreach (var glyph in glyphs.Where(glyph => glyph.PositionInString >= 0))
        {
            Assert.That(dots.All(dot => dot.Rect.Top >= glyph.Rect.Bottom - 0.5), Is.True,
                $"the ellipsis below {glyph.Symbol}");
        }
    }

    [TestCase(VerticalTextAlignment.Top, 1)]
    [TestCase(VerticalTextAlignment.Center, 0)]
    [TestCase(VerticalTextAlignment.Bottom, -1)]
    public void VerticalAlignment_PlacesTheLinesAcrossTheArea(VerticalTextAlignment alignment, int side)
    {
        var glyph = Lay("漢", 200, 400, across: alignment).GetTextData()[0];
        var expected = side switch
        {
            1 => 200 - LineHeight / 2,
            0 => 100,
            _ => LineHeight / 2,
        };

        Assert.That(UprightCenter(glyph), Is.EqualTo(expected).Within(0.01));
    }

    [Test]
    public void Underline_RunsRightOfTheLine_StrikethroughDownItsMiddle()
    {
        var layout = new TextLayout(NotoSansCjk.Value, Font) { Fallback = null, WritingMode = WritingMode.VerticalRightToLeft };
        var attributes = new TextAttributes { Decorations = TextDecorations.Underline | TextDecorations.Strikethrough };
        layout.ProcessText(new AttributedText("漢字", attributes), FontSize, new Size<double>(200, 400), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var adornments = layout.GetAdornments();
        var underline = adornments.Single(adornment => adornment.Kind == TextAdornmentKind.Underline).Rect;
        var strikethrough = adornments.Single(adornment => adornment.Kind == TextAdornmentKind.Strikethrough).Rect;
        var center = 200 - LineHeight / 2;
        var glyphsRight = layout.GetTextData().Max(glyph => glyph.Rect.Right);

        Assert.That(underline.Height, Is.EqualTo(2 * FontSize).Within(0.01), "down the whole text");
        Assert.That(underline.Left, Is.GreaterThanOrEqualTo(glyphsRight - 0.5), "right of the glyphs");
        Assert.That(underline.Right, Is.LessThanOrEqualTo(200), "inside the line");
        Assert.That(strikethrough.Center.X, Is.EqualTo(center).Within(0.01));
    }

    [TestCase("2024年")]
    [TestCase("Room 12")]
    [TestCase("3.5倍")]
    public void LongerNumbersAndNumbersAmongLatin_LieTurned(string text)
    {
        var digits = Lay(text, 200, 400).GetTextData().Where(glyph => char.IsAsciiDigit(glyph.Symbol)).ToArray();

        Assert.That(digits, Is.Not.Empty);
        Assert.That(digits.All(glyph => glyph.Sideways), Is.True);
    }

    [Test]
    public void UprightPunctuation_TakesItsVerticalForm()
    {
        var vertical = TextShaper.Shape(Font, "。", new ShapingOptions(vertical: true))[0].GlyphIndex;
        var across = TextShaper.Shape(Font, "。", ShapingOptions.Default)[0].GlyphIndex;

        Assert.That(vertical, Is.Not.EqualTo(across));
        Assert.That(Lay("。", 200, 400).GetTextData()[0].Glyph.Index, Is.EqualTo(vertical));
    }

    [TestCase("「", 0x300C)]
    [TestCase("ー", 0x30FC)]
    [TestCase("（", 0xFF08)]
    public void BracketsAndLongMarks_StandUprightInTheFontsVerticalForm(string text, int codepoint)
    {
        var vertical = TextShaper.Shape(Font, text, new ShapingOptions(vertical: true))[0].GlyphIndex;
        var glyph = Lay(text + "漢", 200, 400).GetTextData()[0];

        Assert.That(glyph.Upright, Is.True, $"U+{codepoint:X4}");
        Assert.That(glyph.Glyph.Index, Is.EqualTo(vertical));
    }

    [TestCase(HorizontalTextAlignment.Left, 0)]
    [TestCase(HorizontalTextAlignment.Center, 130)]
    [TestCase(HorizontalTextAlignment.Right, 260)]
    public void Alignment_PlacesTheTextAlongTheLine(HorizontalTextAlignment alignment, double top)
    {
        var glyphs = Lay("漢字", 200, 300, alignment).GetTextData();

        Assert.That(glyphs[0].PenX, Is.EqualTo(top).Within(1e-3));
        Assert.That(glyphs[1].PenX, Is.EqualTo(top + FontSize).Within(1e-3));
    }

    // Tracking is room after a letter: the cell an upright letter takes down its line ends where the letter does.
    [Test]
    public void AJustifiedLineWithTracking_EndsItsLastLetterAtTheEnd()
    {
        var layout = new TextLayout(NotoSansCjk.Value, Font)
        {
            Fallback = null, WritingMode = WritingMode.VerticalRightToLeft, Tracking = 200,
            LetterSpacing = new SpacingRange(0, 0, 1), HangingPunctuation = HangingPunctuation.None,
        };
        layout.ProcessText(string.Concat(Enumerable.Repeat("漢字かな、", 12)), FontSize, new Size<double>(400, 301),
            TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);
        var glyphs = layout.GetTextData();
        var tail = 200 * FontSize / 1000;

        Assume.That(layout.LineCount, Is.GreaterThan(2));
        for (var line = 0; line < layout.LineCount - 1; line++)
        {
            var last = glyphs.Where(glyph => glyph.LineIndex == line && glyph.PositionInString >= 0)
                .OrderBy(glyph => glyph.PenX).Last();
            Assert.That(last.PenX + last.Advance - tail, Is.EqualTo(301).Within(0.5), $"line {line}");
        }
    }

    [Test]
    public void Size_IsTheLinesAcrossByTheLongestLineDown()
    {
        var layout = Lay("漢字\n漢", double.NaN, double.NaN);
        var glyphs = layout.GetTextData();

        Assert.That(layout.CalculatedLayoutSize.Width, Is.EqualTo(Math.Ceiling(2 * LineHeight)));
        Assert.That(layout.CalculatedLayoutSize.Height, Is.EqualTo(2 * FontSize));
        Assert.That(UprightCenter(glyphs[0]), Is.EqualTo(Math.Ceiling(2 * LineHeight) - LineHeight / 2).Within(0.01));
        Assert.That(UprightCenter(glyphs[2]), Is.EqualTo(Math.Ceiling(2 * LineHeight) - 1.5 * LineHeight).Within(0.01));
    }

    [Test]
    public void Frames_AreFilledInTurn_EachFromItsRight()
    {
        var text = string.Concat(Enumerable.Repeat("漢字", 20));
        RectangleF[] frames = [new RectangleF(100, 0, 100, 100), new RectangleF(0, 0, 100, 100)];
        var layout = new TextLayout(NotoSansCjk.Value, Font)
        {
            Fallback = null, WritingMode = WritingMode.VerticalRightToLeft, Frames = frames,
        };
        layout.ProcessText(text, FontSize, new Size<double>(200, 100), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var linesAFrame = (int)(100 / LineHeight);

        Assert.That(layout.OversetIndex, Is.EqualTo(2 * linesAFrame * 5));
        foreach (var glyph in layout.GetTextData())
        {
            var frame = frames[glyph.PositionInString < linesAFrame * 5 ? 0 : 1];
            var line = glyph.LineIndex % linesAFrame;
            Assert.That(UprightCenter(glyph), Is.EqualTo(frame.Right - (line + 0.5) * LineHeight).Within(0.01),
                $"{glyph.PositionInString} in frame {Array.IndexOf(frames, frame)}");
        }
    }

    [Test]
    public void RangeRects_RunDownTheLine()
    {
        var rect = Lay("漢字かな", 200, 400).GetRangeRects(0, 2).Single();

        Assert.That(rect.X, Is.EqualTo(200 - LineHeight).Within(0.01));
        Assert.That(rect.Width, Is.EqualTo(LineHeight).Within(0.01));
        Assert.That(rect.Y, Is.EqualTo(0).Within(0.01));
        Assert.That(rect.Height, Is.EqualTo(2 * FontSize).Within(0.01));
    }

    // A click where a character is shown finds it, on every line, its upper half before it and its lower half after.
    [Test]
    public void AClick_FindsTheCharacterWhereItIsShown()
    {
        const string text = "漢字かなカナ漢字かなカナ漢字かな";
        var layout = Lay(text, 200, 100);

        Assert.That(layout.LineCount, Is.GreaterThan(2));
        for (var i = 0; i < text.Length; i++)
        {
            var rect = layout.GetRangeRects(i, i + 1).Single();
            var x = rect.X + rect.Width / 2;
            var upper = layout.HitTest(x, rect.Y + rect.Height * 0.25);
            var lower = layout.HitTest(x, rect.Y + rect.Height * 0.75);

            Assert.That((upper.Index, upper.CaretIndex, upper.IsInside), Is.EqualTo((i, i, true)), $"the upper half of {i}");
            Assert.That((lower.Index, lower.CaretIndex, lower.IsInside), Is.EqualTo((i, i + 1, true)), $"the lower half of {i}");
        }
    }

    // The caret lies across its line, at the top of the character after it.
    [Test]
    public void TheCaret_LiesAcrossItsLine()
    {
        var layout = Lay("漢字かな", 200, 400);
        var caret = layout.GetCaretRect(new CaretPosition(2), 1);
        var line = layout.GetRangeRects(0, 4).Single();

        Assert.That(caret.X, Is.EqualTo(line.X).Within(0.01));
        Assert.That(caret.Width, Is.EqualTo(line.Width).Within(0.01));
        Assert.That(caret.Y, Is.EqualTo(2 * FontSize).Within(0.01));
        Assert.That(caret.Height, Is.EqualTo(1).Within(1e-6));
    }

    // Left of a line is the next one: where the caret stands down it is kept.
    [Test]
    public void TheLineAfter_KeepsTheCaretsPlaceDownTheLine()
    {
        var layout = Lay("漢字かなカナ漢字かなカ", 200, 100);
        var (down, line) = layout.GetCaretPoint(new CaretPosition(2));
        var hit = layout.HitTestLine(line + 1, down);

        Assert.That(hit.CaretIndex, Is.EqualTo(7), "two characters down the next line");
    }

    [Test]
    public void AClickOnADropCap_FindsIt_OnEveryLineItSpans()
    {
        var layout = new TextLayout(NotoSansCjk.Value, Font)
        {
            Fallback = null, WritingMode = WritingMode.VerticalRightToLeft, DropCap = new DropCap(3),
        };
        layout.ProcessText(string.Concat(Enumerable.Repeat("漢字かな", 20)), FontSize, new Size<double>(1000, 200),
            TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var cap = layout.GetTextData().Single(glyph => glyph.PositionInString == 0);

        for (var line = 0; line < 3; line++)
        {
            var x = 1000 - (line + 0.5) * LineHeight;
            var hit = layout.HitTest(x, cap.FontSize * 0.25);
            Assert.That((hit.Index, hit.CaretIndex), Is.EqualTo((0, 0)), $"the cap from line {line}");
        }
    }

    [Test]
    public void HorizontalText_HasNoTurnedOrUprightGlyphs()
    {
        var layout = new TextLayout(NotoSansCjk.Value, Font) { Fallback = null };
        layout.ProcessText("漢字Abc12", FontSize, new Size<double>(400, 200), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetTextData().Any(glyph => glyph.Upright || glyph.Sideways), Is.False);
    }

    [TestCase(2)]
    [TestCase(3)]
    public void ADropCap_StandsAtTheHeadOfItsLines_ItsEmAcrossThemAll(int lines)
    {
        var layout = new TextLayout(NotoSansCjk.Value, Font)
        {
            Fallback = null, WritingMode = WritingMode.VerticalRightToLeft, DropCap = new DropCap(lines),
        };
        layout.ProcessText(string.Concat(Enumerable.Repeat("漢字かな", 20)), FontSize, new Size<double>(1000, 200),
            TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var glyphs = layout.GetTextData();
        var cap = glyphs.Single(glyph => glyph.PositionInString == 0);
        var right = 1000 - LineHeight / 2 + FontSize / 2;
        var left = 1000 - (lines - 0.5) * LineHeight - FontSize / 2;

        Assert.That(cap.Upright, Is.True);
        Assert.That(cap.FontSize, Is.EqualTo(right - left).Within(1e-3), "its em from the first line's to the last's");
        Assert.That(UprightCenter(cap), Is.EqualTo((right + left) / 2).Within(0.01));
        Assert.That(cap.Rect.Top, Is.GreaterThan(-0.5));
        Assert.That(cap.Rect.Bottom, Is.LessThan(cap.FontSize + 0.5), "inside its em at the head of the lines");
        for (var line = 0; line <= lines; line++)
        {
            var head = glyphs.Where(glyph => glyph.LineIndex == line && glyph.PositionInString > 0).Min(glyph => glyph.PenX);
            Assert.That(head, Is.EqualTo(line < lines ? cap.FontSize : 0).Within(1e-3),
                line < lines ? "the lines beside it start below it" : "the next line at the head");
        }
    }

    private static TextLayout Lay(string text, double width, double height,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left,
        VerticalTextAlignment across = VerticalTextAlignment.Top, TextWrapping wrapping = TextWrapping.WrapByWords,
        TextTrimming trimming = TextTrimming.None)
    {
        var layout = new TextLayout(NotoSansCjk.Value, Font) { Fallback = null, WritingMode = WritingMode.VerticalRightToLeft };
        layout.ProcessText(text, FontSize, new Size<double>(width, height), wrapping, trimming, alignment, across);
        return layout;
    }

    private static double UprightCenter(GlyphWordData glyph)
    {
        var scale = glyph.FontSize / glyph.Font.UnitsPerEm;
        return glyph.Rect.X - glyph.Font.GetLeftSideBearing(glyph.Glyph.Index) * scale
               + glyph.Font.GetAdvanceWidth(glyph.Glyph.Index) * scale / 2;
    }
}
