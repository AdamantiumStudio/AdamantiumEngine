using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A vertical alignment places whole lines, as WinUI's: a text whose height is its own stays where it is, and
/// its lines, selection and backgrounds stay inside its size. <see cref="TextLineBounds"/> trims the box to the
/// capitals of the first line and the baseline of the last.</summary>
[TestFixture]
public class TextLineBoundsTests
{
    private const double Size = 100;
    private const string Text = "Hamburg";

    private static IFont Font()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf");
        return Typeface.LoadFont(path).Fonts[0];
    }

    private static TextLayout Lay(string text, VerticalTextAlignment alignment, double height = double.NaN,
        TextLineBounds bounds = TextLineBounds.Full, Action<TextLayout> setUp = null)
    {
        var font = Font();
        var layout = new TextLayout(font.Typeface, font) { Fallback = null, LineBounds = bounds };
        setUp?.Invoke(layout);
        layout.ProcessText(text, Size, new Size<double>(1000, height), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, alignment);
        return layout;
    }

    private static double Cap(IFont font) => font.CapsHeight * Size / font.UnitsPerEm;

    [TestCase(VerticalTextAlignment.Center)]
    [TestCase(VerticalTextAlignment.Bottom)]
    public void AnAlignment_DoesNotMoveTheLinesOfATextAsHighAsThemselves(VerticalTextAlignment alignment)
    {
        var top = Lay(Text + "\n" + Text, VerticalTextAlignment.Top);
        var layout = Lay(Text + "\n" + Text, alignment);

        Assert.That(layout.GetLine(0).Top, Is.EqualTo(0));
        Assert.That(layout.GetLine(1).Baseline, Is.EqualTo(top.GetLine(1).Baseline));
        Assert.That(layout.GetTextData().Select(x => x.Rect.Y), Is.EqualTo(top.GetTextData().Select(x => x.Rect.Y)));
        foreach (var rect in layout.GetRangeRects(0, layout.Text.Length))
        {
            Assert.That(rect.Y, Is.GreaterThanOrEqualTo(0));
            Assert.That(rect.Y + rect.Height, Is.LessThanOrEqualTo(layout.CalculatedLayoutSize.Height + 1e-3),
                "the selection stays inside the text's size");
        }
    }

    [Test]
    public void Center_PutsTheWholeLinesInTheMiddleOfTheArea()
    {
        var layout = Lay(Text, VerticalTextAlignment.Center, 300);
        var line = layout.GetLine(0);

        Assert.That(line.Top + line.Height / 2, Is.EqualTo(150).Within(1e-3));
    }

    [Test]
    public void Bottom_PutsTheLastLineOnTheBottomEdge()
    {
        var layout = Lay(Text + "\n" + Text, VerticalTextAlignment.Bottom, 400);
        var line = layout.GetLine(1);

        Assert.That(line.Top + line.Height, Is.EqualTo(400).Within(1e-3));
    }

    [Test]
    public void Tight_IsFromTheTopOfTheCapitalsToTheBaseline()
    {
        var full = Lay(Text, VerticalTextAlignment.Top);
        var layout = Lay(Text, VerticalTextAlignment.Top, bounds: TextLineBounds.Tight);
        var line = layout.GetLine(0);
        var cap = Cap(layout.Font);

        Assert.That(line.Baseline - cap, Is.InRange(0, 1), "the capitals start at the top, within a unit");
        Assert.That(layout.CalculatedLayoutSize.Height - line.Baseline, Is.InRange(0, 1), "the box ends on the baseline");
        Assert.That(line.Top, Is.EqualTo(0));
        Assert.That(line.Top + line.Height - line.Baseline, Is.InRange(0, 1), "the line ends on its baseline");
        var trim = full.GetLine(0).Baseline - line.Baseline;
        Assert.That(layout.GetTextData().Select(x => x.Rect.Y),
            Is.EqualTo(full.GetTextData().Select(x => (float)(x.Rect.Y - trim))), "the letters moved up with the line");
    }

    [Test]
    public void TrimToCapHeight_KeepsTheLinesApart()
    {
        var full = Lay(Text + "\n" + Text, VerticalTextAlignment.Top);
        var layout = Lay(Text + "\n" + Text, VerticalTextAlignment.Top, bounds: TextLineBounds.TrimToCapHeight);

        Assert.That(layout.GetLine(1).Baseline - layout.GetLine(0).Baseline,
            Is.EqualTo(full.GetLine(1).Baseline - full.GetLine(0).Baseline).Within(1e-3));
        Assert.That(layout.GetLine(0).Baseline - Cap(layout.Font), Is.InRange(0, 1));
        Assert.That(full.CalculatedLayoutSize.Height - layout.CalculatedLayoutSize.Height,
            Is.EqualTo(full.GetLine(0).Baseline - layout.GetLine(0).Baseline).Within(1), "only the top was trimmed");
    }

    [Test]
    public void TrimToBaseline_EndsOnTheLastBaseline()
    {
        var layout = Lay(Text + "\n" + Text, VerticalTextAlignment.Top, bounds: TextLineBounds.TrimToBaseline);

        Assert.That(layout.GetLine(0).Top, Is.EqualTo(0));
        Assert.That(layout.CalculatedLayoutSize.Height - layout.GetLine(1).Baseline, Is.InRange(0, 1));
    }

    [Test]
    public void TrimToBaseline_NeverMakesTheBoxTaller()
    {
        var full = Lay(Text + "\n" + Text, VerticalTextAlignment.Top, setUp: l => l.LineHeight = 60);
        var trimmed = Lay(Text + "\n" + Text, VerticalTextAlignment.Top, bounds: TextLineBounds.TrimToBaseline,
            setUp: l => l.LineHeight = 60);

        Assert.That(trimmed.CalculatedLayoutSize.Height, Is.LessThanOrEqualTo(full.CalculatedLayoutSize.Height));
    }

    [Test]
    public void TrimToCapHeight_KeepsAnObjectTallerThanTheCapitalsInside()
    {
        var font = Font();
        var layout = new TextLayout(font.Typeface, font) { Fallback = null, LineBounds = TextLineBounds.Tight };
        var attributed = new AttributedText("ab" + (char)0xFFFC + "cd");
        attributed.Apply(2, 1, new TextAttributes { ObjectSize = new Size<double>(40, 150) });
        layout.ProcessText(attributed, Size, new Size<double>(1000, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetInlineObjects().Single().Rect.Y, Is.InRange(0, 1));
    }

    [Test]
    public void Center_TakesTheTrimmedEmptyLastLine()
    {
        var layout = Lay(Text + "\n", VerticalTextAlignment.Center, 300, TextLineBounds.TrimToBaseline);
        var first = layout.GetLine(0);
        var last = layout.GetLine(1);

        Assert.That((first.Top + last.Top + last.Height) / 2, Is.EqualTo(150).Within(1e-3));
    }

    [Test]
    public void Tight_AndCenter_PutTheCapitalsInTheMiddle()
    {
        var layout = Lay(Text, VerticalTextAlignment.Center, 300, TextLineBounds.Tight);
        var baseline = layout.GetLine(0).Baseline;

        Assert.That(baseline - Cap(layout.Font) / 2, Is.EqualTo(150).Within(1));
    }
}
