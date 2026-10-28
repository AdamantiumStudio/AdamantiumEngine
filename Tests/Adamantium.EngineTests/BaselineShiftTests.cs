using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Baseline shift, as InDesign's: a range of text raised or lowered off its line's baseline, its size and the
/// line's height unchanged, its lines and its hyphens following it.</summary>
[TestFixture]
public class BaselineShiftTests
{
    private const double FontSize = 20;

    private static TextLayout Lay(string text, int start, int length, TextAttributes attributes, double width = double.NaN,
        string font = "SourceSans3-Regular.ttf", WritingMode mode = WritingMode.Horizontal, Hyphens hyphens = Hyphens.None,
        Action<TextLayout> setUp = null)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", font));
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, WritingMode = mode, Hyphens = hyphens, Language = "en-US",
        };
        setUp?.Invoke(layout);
        var attributed = new AttributedText(text);
        attributed.Apply(start, length, attributes);
        layout.ProcessText(attributed, FontSize, new Size<double>(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    [TestCase(6)]
    [TestCase(-4)]
    public void AShiftedRange_MovesUpOrDown_TheRestStays(double shift)
    {
        var plain = Lay("E=mc2 here", 4, 1, new TextAttributes()).GetTextData();
        var shifted = Lay("E=mc2 here", 4, 1, new TextAttributes { BaselineShift = shift }).GetTextData();

        for (var i = 0; i < plain.Length; i++)
        {
            var expected = plain[i].PositionInString == 4 ? plain[i].Rect.Y - shift : plain[i].Rect.Y;
            Assert.That(shifted[i].Rect.Y, Is.EqualTo(expected).Within(1e-3), $"glyph {i} ({plain[i].Symbol})");
            Assert.That(shifted[i].Rect.X, Is.EqualTo(plain[i].Rect.X).Within(1e-3), "nothing moves along the line");
        }
    }

    [Test]
    public void TheLine_KeepsItsHeight()
    {
        const string text = "first line\nsecond line";
        var plain = Lay(text, 0, 5, new TextAttributes());
        var shifted = Lay(text, 0, 5, new TextAttributes { BaselineShift = 12 });

        Assert.That(shifted.LineCount, Is.EqualTo(plain.LineCount));
        for (var line = 0; line < plain.LineCount; line++)
        {
            Assert.That(shifted.GetLine(line).Top, Is.EqualTo(plain.GetLine(line).Top).Within(1e-6));
            Assert.That(shifted.GetLine(line).Height, Is.EqualTo(plain.GetLine(line).Height).Within(1e-6));
        }
    }

    [Test]
    public void ItsUnderline_FollowsIt_ItsBackgroundStaysOnTheLine()
    {
        var attributes = new TextAttributes
        {
            Decorations = TextDecorations.Underline, Background = new Color(255, 255, 0, 255),
        };
        var plain = Lay("abc def", 0, 3, attributes).GetAdornments();
        var shifted = Lay("abc def", 0, 3, attributes.With(new TextAttributes { BaselineShift = 5 })).GetAdornments();

        var line = plain.Single(adornment => adornment.Kind == TextAdornmentKind.Underline).Rect;
        var raised = shifted.Single(adornment => adornment.Kind == TextAdornmentKind.Underline).Rect;
        Assert.That(raised.Y, Is.EqualTo(line.Y - 5).Within(1e-3));
        Assert.That(shifted.Single(adornment => adornment.Kind == TextAdornmentKind.Background).Rect,
            Is.EqualTo(plain.Single(adornment => adornment.Kind == TextAdornmentKind.Background).Rect));
    }

    [TestCase(null)]
    [TestCase(9.0)]
    public void TheLastCharactersUnderline_IsDrawn(double? shift)
    {
        var adornments = Lay("note1", 4, 1, new TextAttributes
        {
            Decorations = TextDecorations.Underline, BaselineShift = shift,
        }).GetAdornments();

        Assert.That(adornments.Count(adornment => adornment.Kind == TextAdornmentKind.Underline), Is.EqualTo(1));
    }

    [Test]
    public void AFractionalShift_KeepsTheUnderlineOnTheGlyphsPixelRow()
    {
        var attributes = new TextAttributes { Decorations = TextDecorations.Underline };
        var plain = Lay("abc", 0, 3, attributes);
        var shifted = Lay("abc", 0, 3, attributes.With(new TextAttributes { BaselineShift = 2.5 }));

        var moved = plain.GetTextData()[0].Rect.Y - shifted.GetTextData()[0].Rect.Y;
        var line = plain.GetAdornments().Single().Rect.Y - shifted.GetAdornments().Single().Rect.Y;
        Assert.That(line, Is.EqualTo(moved).Within(1e-3), "the line moves by as many whole pixels as the glyph");
    }

    [Test]
    public void TabLeaders_FollowTheShift()
    {
        void Stops(TextLayout layout) => layout.TabStops = [new TabStop(200, TabAlignment.Left, ".")];
        var plain = Lay("a\tb", 0, 3, new TextAttributes(), setUp: Stops).GetTextData();
        var shifted = Lay("a\tb", 0, 3, new TextAttributes { BaselineShift = 5 }, setUp: Stops).GetTextData();
        var dots = Enumerable.Range(0, plain.Length).Where(i => plain[i].PositionInString < 0).ToArray();

        Assert.That(dots, Is.Not.Empty);
        foreach (var i in dots)
        {
            Assert.That(shifted[i].Rect.Y, Is.EqualTo(plain[i].Rect.Y - 5).Within(1e-3));
        }
    }

    [Test]
    public void ADropCap_FollowsTheShift()
    {
        const string text = "Once upon a time there lived a king whose daughters were all beautiful, but the youngest was " +
                            "so beautiful that the sun itself was astonished whenever it shone in her face.";
        void Cap(TextLayout layout) => layout.DropCap = new DropCap(3);
        var plain = Lay(text, 0, text.Length, new TextAttributes(), 300, setUp: Cap).GetTextData();
        var shifted = Lay(text, 0, text.Length, new TextAttributes { BaselineShift = 4 }, 300, setUp: Cap).GetTextData();

        var cap = Array.FindIndex(plain, glyph => glyph.PositionInString == 0);
        var body = Array.FindIndex(plain, glyph => glyph.PositionInString == 1);
        Assert.That(shifted[cap].Rect.Y, Is.EqualTo(plain[cap].Rect.Y - 4).Within(1e-3));
        Assert.That(shifted[body].Rect.Y, Is.EqualTo(plain[body].Rect.Y - 4).Within(1e-3));
    }

    [Test]
    public void AHyphenOfAShiftedWord_IsShiftedWithIt()
    {
        const string text = "Hyphenation of extraordinarily long words";
        var plain = Lay(text, 0, text.Length, new TextAttributes(), 120, hyphens: Hyphens.Auto).GetTextData();
        var shifted = Lay(text, 0, text.Length, new TextAttributes { BaselineShift = 3 }, 120, hyphens: Hyphens.Auto)
            .GetTextData();
        var hyphens = Enumerable.Range(0, plain.Length).Where(i => plain[i].PositionInString < 0).ToArray();

        Assert.That(hyphens, Is.Not.Empty);
        foreach (var i in hyphens)
        {
            Assert.That(shifted[i].Rect.Y, Is.EqualTo(plain[i].Rect.Y - 3).Within(1e-3));
        }
    }

    [Test]
    public void InVerticalText_AnUprightGlyph_MovesAcrossTheLine()
    {
        var plain = Lay("漢字", 1, 1, new TextAttributes(), 200, "NotoSansCJK-Regular.ttc", WritingMode.VerticalRightToLeft)
            .GetTextData();
        var shifted = Lay("漢字", 1, 1, new TextAttributes { BaselineShift = 4 }, 200, "NotoSansCJK-Regular.ttc",
            WritingMode.VerticalRightToLeft).GetTextData();

        Assert.That(shifted[0].Rect, Is.EqualTo(plain[0].Rect));
        Assert.That(shifted[1].Rect.X, Is.EqualTo(plain[1].Rect.X + 4).Within(1e-3), "raised toward the top of the line, the right");
        Assert.That(shifted[1].Rect.Y, Is.EqualTo(plain[1].Rect.Y).Within(1e-3));
    }
}
