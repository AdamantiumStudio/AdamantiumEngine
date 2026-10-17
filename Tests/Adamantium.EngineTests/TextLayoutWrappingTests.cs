using System.IO;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class TextLayoutWrappingTests
{
    private static TextLayout Layout(string text, double width)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText(text, 20, new Size(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static double Width(string text) => Layout(text, double.NaN).GetLine(0).Width;

    [Test]
    public void AHyphenatedWord_WrapsAfterItsHyphen()
    {
        var width = Width("well-") + 2;
        var layout = Layout("well-known", width);
        var stops = layout.GetCaretStops();

        Assert.That(layout.LineCount, Is.EqualTo(2));
        Assert.That(stops[4].LineIndex, Is.EqualTo(0), "the hyphen stays on the first line");
        Assert.That(stops[5].LineIndex, Is.EqualTo(1), "known starts the second");
    }

    [Test]
    public void ANoBreakSpace_KeepsItsWordsTogether()
    {
        var width = Width("one two") + 2;
        var layout = Layout("one two three", width);
        var stops = layout.GetCaretStops();

        Assert.That(stops[4].LineIndex, Is.EqualTo(1), "two goes down with three");
        Assert.That(stops[8].LineIndex, Is.EqualTo(1));
    }

    [Test]
    public void AWordAfterATab_WrapsToTheLineStart()
    {
        var width = Width("one\t") + 2;
        var layout = Layout("one\ttwo", width);
        var stops = layout.GetCaretStops();

        Assert.That(stops[3].LineIndex, Is.EqualTo(0), "the tab stays on the first line");
        Assert.That(stops[4].LineIndex, Is.EqualTo(1));
        Assert.That(stops[4].X, Is.EqualTo(0).Within(1e-6));
    }

    // Leading spaces and a word too long to follow them: the spaces are a line of their own, which has no ink to align.
    [TestCase(HorizontalTextAlignment.Right)]
    [TestCase(HorizontalTextAlignment.Center)]
    public void ALineOfOnlySpaces_IsAlignedWithoutThrowing(HorizontalTextAlignment alignment)
    {
        var width = Width("Supercalifragilistic") + 2;
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]);

        Assert.DoesNotThrow(() => layout.ProcessText("      Supercalifragilistic", 20, new Size(width, double.NaN),
            TextWrapping.WrapByWords, TextTrimming.None, alignment, VerticalTextAlignment.Top));
        Assert.That(layout.LineCount, Is.EqualTo(2), "the word goes down to a line of its own");
    }

    // Untrimmed, the line that starts inside is kept and cut by the area, as CSS clips it.
    [TestCase(TextTrimming.None, 3)]
    [TestCase(TextTrimming.WordEllipses, 2)]
    [TestCase(TextTrimming.CharEllipses, 2)]
    public void InAHeightOfTwoAndAHalfLines_TrimmingKeepsOnlyWholeLines(TextTrimming trimming, int lines)
    {
        const string text = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen";
        var line = Layout("one", double.NaN).GetLine(0).Height;
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText(text, 20, new Size(Width("one two three") + 2, line * 2.5), TextWrapping.WrapByWords,
            trimming, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.LineCount, Is.EqualTo(lines));
    }

    [Test]
    public void ANumber_StaysWithTheIdeographOrKanaAfterIt()
    {
        const string text = "今日は2026年10月17日で、3つのりんごと12個のみかんを買いました。";
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "NotoSansCJK-Regular.ttc"));
        for (var width = 40; width <= 400; width += 7)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
            layout.ProcessText(text, 20, new Size(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

            for (var line = 1; line < layout.LineCount; line++)
            {
                var start = layout.GetLine(line).Start;
                Assert.That(char.IsDigit(text[start - 1]), Is.False, $"width {width}: line {line} starts {text[start..]}");
            }
        }
    }

    [Test]
    public void ClosingPunctuation_NeverStartsALine()
    {
        var width = Width("(one two") + 2;
        var layout = Layout("(one two), three", width);
        var stops = layout.GetCaretStops();

        Assert.That(stops[8].LineIndex, Is.EqualTo(stops[7].LineIndex), "the parenthesis stays with its word");
        Assert.That(stops[9].LineIndex, Is.EqualTo(stops[7].LineIndex), "and so does the comma");
    }
}
