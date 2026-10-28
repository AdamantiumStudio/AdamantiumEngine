using System.IO;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class TextLayoutNavigationTests
{
    private static TextLayout Layout(string text)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { EmitNewlineCarets = true };
        layout.ProcessText(text, 20, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    [Test]
    public void TheCaretStepsOverAnEmojiAndALetterWithMarksInOneStep()
    {
        var layout = Layout("a\U0001F468‍\U0001F469b éx");

        Assert.That(layout.NextCaretStop(1), Is.EqualTo(6), "the family emoji, five UTF-16 units");
        Assert.That(layout.PreviousCaretStop(6), Is.EqualTo(1));
        Assert.That(layout.NextCaretStop(8), Is.EqualTo(10), "e and its accent");
        Assert.That(layout.PreviousCaretStop(0), Is.EqualTo(0));
        Assert.That(layout.NextCaretStop(11), Is.EqualTo(11), "the end stays the end");
    }

    [Test]
    public void APointPicksTheNearerEdgeOfTheGraphemeUnderIt()
    {
        var layout = Layout("To be");
        var stops = layout.GetCaretStops();
        var line = layout.GetLine(0);
        var y = line.Top + line.Height / 2;

        var leading = layout.HitTest(stops[1].X + 1, y);
        var trailing = layout.HitTest(stops[2].X - 1, y);

        Assert.That((leading.Index, leading.IsTrailing, leading.CaretIndex), Is.EqualTo((1, false, 1)));
        Assert.That((trailing.Index, trailing.IsTrailing, trailing.CaretIndex), Is.EqualTo((1, true, 2)));
        Assert.That(leading.IsInside, Is.True);
    }

    [Test]
    public void APointPastTheLineEnd_PutsTheCaretBeforeItsNewline()
    {
        var layout = Layout("ab\ncd");
        var first = layout.GetLine(0);
        var second = layout.GetLine(1);

        var past = layout.HitTest(500, first.Top + 1);
        var below = layout.HitTest(500, second.Top + second.Height + 50);

        Assert.That(past.CaretIndex, Is.EqualTo(2));
        Assert.That(past.IsInside, Is.False);
        Assert.That(below.CaretIndex, Is.EqualTo(5), "below the text: the end of the last line");
    }

    [Test]
    public void ADoubleClickSelectsTheWordAndCtrlArrowsGoFromWordToWord()
    {
        var layout = Layout("one, two  three");

        Assert.That(layout.GetWordAt(1), Is.EqualTo((0, 3)));
        Assert.That(layout.GetWordAt(6), Is.EqualTo((5, 8)));
        Assert.That(layout.NextWordStop(0), Is.EqualTo(3), "the comma is a stop of its own");
        Assert.That(layout.NextWordStop(3), Is.EqualTo(5), "past the space to the next word");
        Assert.That(layout.NextWordStop(5), Is.EqualTo(10), "past both spaces");
        Assert.That(layout.PreviousWordStop(10), Is.EqualTo(5));
        Assert.That(layout.PreviousWordStop(5), Is.EqualTo(3));
    }
}
