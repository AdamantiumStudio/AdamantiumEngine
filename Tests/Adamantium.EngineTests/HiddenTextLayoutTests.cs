using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Hidden text, as Word's: laid as nothing - no width, no height, no tab or line break - its characters
/// keeping their places, the caret stepping over it with the character after it.</summary>
[TestFixture]
public class HiddenTextLayoutTests
{
    private const double Tolerance = 1e-4;
    private static readonly TextAttributes Hidden = new() { Hidden = true };

    private static TextLayout NewLayout()
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        return new TextLayout(typeface, typeface.Fonts[0]);
    }

    private static TextLayout Laid(AttributedText text, double width = double.NaN,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left)
    {
        var layout = NewLayout();
        var wrapping = double.IsNaN(width) ? TextWrapping.NoWrap : TextWrapping.WrapByWords;
        layout.ProcessText(text, 20, new Size<double>(width, double.NaN), wrapping, TextTrimming.None, alignment,
            VerticalTextAlignment.Top);
        return layout;
    }

    private static double X(TextLayout layout, int index) => layout.GetCaretPoint(new CaretPosition(index)).X;

    [Test]
    public void HiddenText_TakesNoWidth_ItsPlacesAllAtOneSpot()
    {
        var plain = Laid(new AttributedText("abc"));
        var hidden = Laid(new AttributedText("abXYZc").Apply(2, 3, Hidden));

        Assert.Multiple(() =>
        {
            Assert.That(X(hidden, 6), Is.EqualTo(X(plain, 3)).Within(Tolerance), "as wide as the text shown");
            Assert.That(new[] { X(hidden, 3), X(hidden, 4), X(hidden, 5) },
                Has.All.EqualTo(X(hidden, 2)).Within(Tolerance));
            Assert.That(X(hidden, 5), Is.EqualTo(X(plain, 2)).Within(Tolerance), "the shown character after it");
            Assert.That(hidden.GetTextData().Where(glyph => glyph.PositionInString is >= 2 and < 5)
                .Sum(glyph => glyph.Advance), Is.Zero);
        });
    }

    [Test]
    public void TheCaret_StepsOverHiddenText_WithTheCharacterAfterIt()
    {
        var layout = Laid(new AttributedText("abXYZcd").Apply(2, 3, Hidden));

        Assert.Multiple(() =>
        {
            Assert.That(layout.NextCaretStop(1), Is.EqualTo(2), "before it");
            Assert.That(layout.NextCaretStop(2), Is.EqualTo(6), "over it and the c, as one");
            Assert.That(layout.PreviousCaretStop(6), Is.EqualTo(2));
            Assert.That(layout.HitTest(X(layout, 6) - 0.5, 5).CaretIndex, Is.EqualTo(6), "the c's right half");
            Assert.That(layout.HitTest(X(layout, 2) + 0.5, 5).CaretIndex, Is.EqualTo(2), "the c's left half: before it all");
        });
    }

    [Test]
    public void AHiddenTabOrLineBreak_IsNone()
    {
        var plain = Laid(new AttributedText("ab"));
        var tab = Laid(new AttributedText("a\tb").Apply(1, 1, Hidden));
        var broken = Laid(new AttributedText("a" + (char)0x2028 + "b").Apply(1, 1, Hidden));

        Assert.Multiple(() =>
        {
            Assert.That(X(tab, 3), Is.EqualTo(X(plain, 2)).Within(Tolerance), "no tab's width");
            Assert.That(broken.LineCount, Is.EqualTo(1), "no break");
            Assert.That(X(broken, 3), Is.EqualTo(X(plain, 2)).Within(Tolerance));
        });
    }

    [Test]
    public void HiddenText_RaisesNoLine_NorWidensAJustifiedOne()
    {
        var plain = Laid(new AttributedText("ab"));
        var tall = Laid(new AttributedText("abc").Apply(1, 1, new TextAttributes { Hidden = true, FontSize = 80 }));
        const string words = "one two three four five six seven eight nine ten eleven twelve";
        var justified = Laid(new AttributedText(words), 300, HorizontalTextAlignment.Justify);
        var withHidden = Laid(new AttributedText("one two three four" + " gone " + "five six seven eight nine ten eleven twelve")
            .Apply(19, 5, Hidden), 300, HorizontalTextAlignment.Justify);

        Assert.Multiple(() =>
        {
            Assert.That(tall.GetLine(0).Height, Is.EqualTo(plain.GetLine(0).Height).Within(Tolerance));
            Assert.That(withHidden.LineCount, Is.EqualTo(justified.LineCount));
            Assert.That(X(withHidden, 24), Is.EqualTo(X(justified, 19)).Within(0.5),
                "\"five\" where it stands without the hidden word");
        });
    }
}
