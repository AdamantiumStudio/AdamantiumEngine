using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>U+2028 LINE SEPARATOR, as WPF's LineBreak: it ends the line but not the paragraph, so the paragraph keeps
/// its direction across it; a justified line ending in it is not stretched, as one before a newline.</summary>
[TestFixture]
public class LineSeparatorTests
{
    private const char Separator = (char)0x2028;

    private static TextLayout Lay(string text, double width = double.NaN,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left, string font = "SourceSans3-Regular.ttf")
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", font));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        layout.ProcessText(text, 20, new Size<double>(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None, alignment,
            VerticalTextAlignment.Top);
        return layout;
    }

    [Test]
    public void ItEndsTheLine_AndDrawsNothing()
    {
        var layout = Lay("one" + Separator + "two");

        Assert.That(layout.LineCount, Is.EqualTo(2));
        Assert.That(layout.GetLine(1).Start, Is.EqualTo(4));
        Assert.That(layout.GetTextData().Where(glyph => glyph.PositionInString == 3).Select(glyph => glyph.Rect.Width),
            Is.All.EqualTo(0), "the separator has no ink");
    }

    [Test]
    public void TheParagraph_KeepsItsDirectionAcrossIt_ANewlineStartsAnother()
    {
        const string hebrew = "שלום";
        var separated = Lay(hebrew + Separator + "abc", font: "NotoSansHebrew-Regular.ttf");
        var newline = Lay(hebrew + "\nabc", font: "NotoSansHebrew-Regular.ttf");

        Assert.That(separated.IsRightToLeftParagraph(hebrew.Length + 1), Is.True, "still the Hebrew paragraph");
        Assert.That(newline.IsRightToLeftParagraph(hebrew.Length + 1), Is.False, "a paragraph of its own");
    }

    [Test]
    public void AJustifiedLineEndingInIt_IsNotStretched()
    {
        var layout = Lay("aa bb" + Separator + "cc dd ee ff gg hh ii jj kk ll mm nn", 160, HorizontalTextAlignment.Justify);
        var first = layout.GetTextData().Where(glyph => glyph.LineIndex == 0 && glyph.Symbol is not (' ' or '\n'));

        Assert.That(first.Max(glyph => glyph.Rect.Right), Is.LessThan(100), "the line before it keeps its own width");
    }

    [Test]
    public void TheCaret_StandsAtTheEndOfOneLineAndTheStartOfTheNext()
    {
        var layout = Lay("one" + Separator + "two");
        var stops = layout.GetCaretStops();

        Assert.That(stops[3].LineIndex, Is.EqualTo(0));
        Assert.That(stops[4].LineIndex, Is.EqualTo(1));
    }
}
