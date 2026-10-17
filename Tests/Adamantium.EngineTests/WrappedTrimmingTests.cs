using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Text wrapped by words and trimmed: a word too wide for its line, and the last line the height leaves room
/// for, end in an ellipsis.</summary>
[TestFixture]
public class WrappedTrimmingTests
{
    private const double FontSize = 20;
    private const double Width = 120;
    private const string LongWord = "Supercalifragilisticexpialidocious";

    private static TextLayout Layout(string text, TextTrimming trimming, double height = double.NaN)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, Hyphens = Hyphens.None };
        layout.ProcessText(text, FontSize, new Size(Width, height), TextWrapping.WrapByWords, trimming,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static GlyphWordData[] Dots(TextLayout layout) =>
        layout.GetTextData().Where(g => g.PositionInString < 0 && g.Symbol == '.').ToArray();

    [TestCase(TextTrimming.CharEllipses)]
    [TestCase(TextTrimming.WordEllipses)]
    public void AWordWiderThanItsLine_EndsInAnEllipsis_AndTheTextGoesOn(TextTrimming trimming)
    {
        var layout = Layout($"{LongWord} next", trimming);
        var data = layout.GetTextData();

        Assert.That(Dots(layout).Select(g => g.LineIndex), Is.EqualTo(new[] { 0, 0, 0 }));
        Assert.That(data.Where(g => g.LineIndex == 0).Max(g => g.Rect.Right), Is.LessThanOrEqualTo(Width));
        Assert.That(data.Single(g => g.PositionInString == LongWord.Length + 1).LineIndex, Is.EqualTo(1), "the next word below");
    }

    // A line raised by larger text is known only once laid: it was set past the bottom with the ellipsis on it. Trimmed
    // by words, the large word goes and the line, as high as the others, stays.
    [TestCase(TextTrimming.CharEllipses, 1)]
    [TestCase(TextTrimming.WordEllipses, 2)]
    public void ALineRaisedPastTheBottom_IsNotSet_TheLineAboveEndsInTheEllipsis(TextTrimming trimming, int lines)
    {
        const string text = "one\ntwo BIG\nthree";
        var line = Layout("one", TextTrimming.None).GetLine(0).Height;
        var height = line * 2.4;
        var attributed = new AttributedText(text);
        attributed.Apply(text.IndexOf("BIG", StringComparison.Ordinal), 3, new TextAttributes { FontSize = 50 });
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(attributed, FontSize, new Size(Width, height), TextWrapping.WrapByWords, trimming,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.LineCount, Is.EqualTo(lines));
        Assert.That(Dots(layout).Select(g => g.LineIndex), Is.All.EqualTo(lines - 1));
        Assert.That(layout.GetTextData().Max(g => g.Rect.Bottom), Is.LessThanOrEqualTo(height));
    }

    [Test]
    public void TheEllipsis_IsSetAsTheTextItFollows()
    {
        const string text = "small LARGE LARGE LARGE LARGE";
        var attributed = new AttributedText(text);
        var red = Colors.Red;
        attributed.Apply(6, text.Length - 6, new TextAttributes { FontSize = 30, Foreground = red, BaselineShift = 4 });
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(attributed, FontSize, new Size(Width, double.NaN), TextWrapping.NoWrap,
            TextTrimming.CharEllipses, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var dots = Dots(layout);
        var before = layout.GetTextData().Last(g => g.PositionInString >= 0 && g.Symbol != ' ');

        Assert.That(dots, Has.Length.EqualTo(3));
        Assert.That(dots.Select(g => g.FontSize), Is.All.EqualTo(30));
        Assert.That(dots.Select(g => g.Attributes?.Foreground), Is.All.EqualTo(red));
        Assert.That(dots.Select(g => g.OffsetY), Is.All.EqualTo(before.OffsetY));
        Assert.That(before.OffsetY, Is.Not.EqualTo(0), "the shift is there to follow");
        Assert.That(dots[0].Rect.Bottom, Is.EqualTo(before.Rect.Bottom).Within(2), "on the shifted baseline");
    }

    // Cut to the ellipsis, a word whose letters would raise the last line past the bottom leaves none on it. (A first
    // line that is taller than the area by its own words is set all the same.)
    [Test]
    public void AnEllipsis_TakesNoLettersThatRaiseTheLastLinePastTheBottom()
    {
        const string text = "aaaa BIG ccc";
        var height = Layout("one", TextTrimming.None).GetLine(0).Height * 2.4;
        var attributed = new AttributedText(text);
        attributed.Apply(5, 3, new TextAttributes { FontSize = 80 });
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(attributed, FontSize, new Size(Width, height), TextWrapping.WrapByWords,
            TextTrimming.CharEllipses, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var line = layout.GetLine(layout.LineCount - 1);

        Assert.That(line.Top + line.Height, Is.LessThanOrEqualTo(height + 1));
        Assert.That(Dots(layout), Has.Length.EqualTo(3));
        Assert.That(layout.GetTextData().Any(g => g.PositionInString == 5), Is.False, "no B");
    }

    // Lines that do not wrap never stopped at the bottom: they are not laid again.
    [TestCase(TextWrapping.NoWrap)]
    [TestCase(TextWrapping.WrapBySymbols)]
    public void UnwrappedLines_AreNotCutAtTheBottom(TextWrapping wrapping)
    {
        const string text = "first line\nsecond line\nthird line";
        var height = Layout("one", TextTrimming.None).GetLine(0).Height * 2.4;
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(text, FontSize, new Size(300, height), wrapping, TextTrimming.WordEllipses,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.LineCount, Is.EqualTo(3));
        Assert.That(layout.OversetIndex, Is.EqualTo(text.Length));
    }

    // After letters of a font with no full stop, the dots come from the text's own font, as they did.
    [Test]
    public void AfterAFontWithNoFullStop_TheEllipsisIsTheTextsOwn()
    {
        const string text = "ab 8888888888";
        var emoji = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "NotoColorEmoji.subset.ttf")).Fonts[0];
        Assume.That(emoji.TryGetGlyphIndex('.', out _), Is.False);
        var attributed = new AttributedText(text);
        attributed.Apply(3, text.Length - 3, new TextAttributes { Font = emoji });
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(attributed, FontSize, new Size(Width, double.NaN), TextWrapping.NoWrap,
            TextTrimming.CharEllipses, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var dots = Dots(layout);

        Assert.That(dots, Has.Length.EqualTo(3));
        Assert.That(dots.Select(g => g.Font), Is.All.SameAs(typeface.Fonts[0]));
        Assert.That(dots.Select(g => g.Rect.Width), Is.All.GreaterThan(0), "a drawn full stop");

        var whole = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        whole.ProcessText(attributed, FontSize, new Size(double.NaN, double.NaN), TextWrapping.NoWrap,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        Assert.That(layout.GetLine(0).Height, Is.EqualTo(whole.GetLine(0).Height).Within(1e-3),
            "the dots do not raise the line");
    }

    [Test]
    public void WithoutTrimming_AWordWiderThanItsLine_Overflows()
    {
        var layout = Layout($"{LongWord} next", TextTrimming.None);

        Assert.That(Dots(layout), Is.Empty);
        Assert.That(layout.GetTextData().Where(g => g.LineIndex == 0).Max(g => g.Rect.Right), Is.GreaterThan(Width));
    }

    [TestCase(TextTrimming.CharEllipses)]
    [TestCase(TextTrimming.WordEllipses)]
    public void TheLastLineTheHeightAllows_EndsInAnEllipsis(TextTrimming trimming)
    {
        const string text = "the quick brown fox jumps over the lazy dog and keeps running far away";
        var lineHeight = Layout(text, TextTrimming.None).GetLine(0).Height;
        var layout = Layout(text, trimming, lineHeight * 2);
        var data = layout.GetTextData();

        Assert.That(data.Max(g => g.LineIndex), Is.EqualTo(1), "two lines");
        Assert.That(Dots(layout).Select(g => g.LineIndex), Is.EqualTo(new[] { 1, 1, 1 }));
        Assert.That(data.Max(g => g.Rect.Right), Is.LessThanOrEqualTo(Width));
    }

    // A line of pieces without spaces between them, as a link breaks after its slashes: the earlier lines stay.
    [Test]
    public void WordEllipses_OnALastLineWithoutSpaces_CutsByCharacters()
    {
        const string text = "See https://example.com/aaaa/bbbb/cccc/dddd/eeee/ffff/gggg";
        var lineHeight = Layout(text, TextTrimming.None).GetLine(0).Height;
        var layout = Layout(text, TextTrimming.WordEllipses, lineHeight * 2);
        var data = layout.GetTextData();

        Assert.That(data.Where(g => g.LineIndex == 0).Select(g => g.PositionInString).Take(3), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(Dots(layout).Select(g => g.LineIndex), Is.EqualTo(new[] { 1, 1, 1 }));
        Assert.That(data.Min(g => g.PenX), Is.GreaterThanOrEqualTo(0));
        Assert.That(data.Count(g => g.LineIndex == 1 && g.PositionInString >= 0), Is.GreaterThan(0), "the line keeps what fits");
    }

    [Test]
    public void AWordAfterANewline_IsTrimmed()
    {
        var layout = Layout($"Title\n{LongWord} end", TextTrimming.CharEllipses);
        var data = layout.GetTextData();

        Assert.That(Dots(layout).Select(g => g.LineIndex), Is.EqualTo(new[] { 1, 1, 1 }));
        Assert.That(data.Max(g => g.Rect.Right), Is.LessThanOrEqualTo(Width));
    }

    [Test]
    public void AWordBeforeANewline_IsMeasuredWithoutWhatFollowsIt()
    {
        var text = $"aa Hello\n{LongWord}";
        var layout = Layout(text, TextTrimming.None);

        Assert.That(layout.GetTextData().Single(g => g.PositionInString == 3).LineIndex, Is.EqualTo(0));
    }

    [Test]
    public void NewlinesPastTheHeight_EndTheLastLineInAnEllipsis()
    {
        const string text = "aa\nbb\ncc\ndd";
        var lineHeight = Layout(text, TextTrimming.None).GetLine(0).Height;
        var layout = Layout(text, TextTrimming.CharEllipses, lineHeight * 2);

        Assert.That(layout.GetTextData().Max(g => g.LineIndex), Is.EqualTo(1));
        Assert.That(Dots(layout).Select(g => g.LineIndex), Is.EqualTo(new[] { 1, 1, 1 }));
    }

    [Test]
    public void WordEllipses_OnTheLastLine_DropsTheWordThatDoesNotFit()
    {
        const string text = "the quick brown fox jumps over the lazy dog and keeps running far away";
        var lineHeight = Layout(text, TextTrimming.None).GetLine(0).Height;
        var layout = Layout(text, TextTrimming.WordEllipses, lineHeight * 2);
        var lastLetter = layout.GetTextData().Where(g => g.PositionInString >= 0 && g.LineIndex == 1)
            .Max(g => g.PositionInString);

        Assert.That(text[lastLetter + 1], Is.EqualTo(' '), "the line ends with a whole word");
    }
}
