using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>An object set into the text at a U+FFFC, as WPF's InlineUIContainer: it advances the line by its width, stands
/// on the baseline, raises a line it is taller than, wraps as a word does and draws nothing itself.</summary>
[TestFixture]
public class InlineObjectTests
{
    private const char Object = (char)0xFFFC;

    private static TextLayout Lay(string text, Size<double>? size, double shift = 0, double width = double.NaN,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left, double height = double.NaN,
        VerticalTextAlignment vertical = VerticalTextAlignment.Top, Action<TextLayout> setUp = null)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        setUp?.Invoke(layout);
        var attributed = new AttributedText(text);
        var index = text.IndexOf(Object);
        if (size != null && index >= 0)
        {
            attributed.Apply(index, 1, new TextAttributes { ObjectSize = size, BaselineShift = shift });
        }

        layout.ProcessText(attributed, 20, new Size<double>(width, height), TextWrapping.WrapByWords, TextTrimming.None,
            alignment, vertical);
        return layout;
    }

    private static float LeftOf(TextLayout layout, int index) =>
        layout.GetTextData().Single(glyph => glyph.PositionInString == index).Rect.X;

    [Test]
    public void ItAdvancesTheLine_ByItsWidth()
    {
        var narrow = Lay("ab" + Object + "cd", new Size<double>(10, 10));
        var wide = Lay("ab" + Object + "cd", new Size<double>(40, 10));

        Assert.That(LeftOf(wide, 3) - LeftOf(narrow, 3), Is.EqualTo(30).Within(1e-3));
        Assert.That(wide.GetInlineObjects().Single().Rect.Width, Is.EqualTo(40).Within(1e-3));
    }

    [Test]
    public void ItStandsOnTheBaseline_ASmallOneLeavesTheLineAsItIs()
    {
        var plain = Lay("ab cd", null);
        var layout = Lay("ab" + Object + "cd", new Size<double>(12, 8));
        var placed = layout.GetInlineObjects().Single();

        Assert.That(placed.Index, Is.EqualTo(2));
        Assert.That(placed.Rect.Bottom, Is.EqualTo(layout.GetLine(0).Baseline).Within(1e-3));
        Assert.That(placed.Rect.Height, Is.EqualTo(8).Within(1e-3));
        Assert.That(layout.GetLine(0).Height, Is.EqualTo(plain.GetLine(0).Height).Within(1e-6));
    }

    [Test]
    public void ATallOne_RaisesItsLine()
    {
        var plain = Lay("ab cd\nef", null);
        var layout = Lay("ab" + Object + "cd\nef", new Size<double>(12, 60));
        var placed = layout.GetInlineObjects().Single();

        Assert.That(layout.GetLine(0).Height, Is.GreaterThan(60));
        Assert.That(placed.Rect.Y, Is.EqualTo(layout.GetLine(0).Top).Within(1), "its top is the line's top");
        Assert.That(placed.Rect.Bottom, Is.EqualTo(layout.GetLine(0).Baseline).Within(1e-3));
        Assert.That(layout.GetLine(1).Height, Is.EqualTo(plain.GetLine(1).Height).Within(1e-6), "the next line is not");
        Assert.That(layout.GetTextData().Single(glyph => glyph.PositionInString == 0).Rect.Bottom,
            Is.GreaterThan(plain.GetTextData().Single(glyph => glyph.PositionInString == 0).Rect.Bottom + 30),
            "the text of its line moves down to its baseline");
    }

    [Test]
    public void ABaselineShift_RaisesIt()
    {
        var plain = Lay("ab" + Object + "cd", new Size<double>(12, 8));
        var raised = Lay("ab" + Object + "cd", new Size<double>(12, 8), 5);

        Assert.That(raised.GetInlineObjects().Single().Rect.Y,
            Is.EqualTo(plain.GetInlineObjects().Single().Rect.Y - 5).Within(1));
    }

    [Test]
    public void ItWraps_AsAWord()
    {
        var layout = Lay("word " + Object + " word", new Size<double>(50, 10), width: 70);
        var placed = layout.GetInlineObjects().Single();

        Assert.That(placed.Rect.Y, Is.GreaterThanOrEqualTo(layout.GetLine(1).Top - 1e-3));
        Assert.That(placed.Rect.X, Is.EqualTo(0).Within(1e-3), "it starts the line");
    }

    [Test]
    public void AlignedRight_ItsRightEdgeIsTheLines()
    {
        var layout = Lay("ab" + Object, new Size<double>(30, 10), width: 300, alignment: HorizontalTextAlignment.Right);

        Assert.That(layout.GetInlineObjects().Single().Rect.Right, Is.EqualTo(300).Within(0.5));
    }

    [Test]
    public void CenteredDown_ATallOneIsCenteredWhole()
    {
        var layout = Lay("ab" + Object, new Size<double>(12, 60), height: 100, vertical: VerticalTextAlignment.Center);
        var placed = layout.GetInlineObjects().Single();

        Assert.That(placed.Rect.Y, Is.GreaterThan(0), "not cut off at the top");
        Assert.That(placed.Rect.Y, Is.GreaterThan(10), "not hugging the top either");
    }

    [TestCase(VerticalTextAlignment.Center)]
    [TestCase(VerticalTextAlignment.Bottom)]
    public void ItsBottom_IsTheTextsBaseline_WhereverTheBlockStands(VerticalTextAlignment vertical)
    {
        var layout = Lay("ab" + Object + "cd", new Size<double>(12, 8), height: 101.3, vertical: vertical);

        Assert.That(layout.GetInlineObjects().Single().Rect.Bottom, Is.EqualTo(layout.GetLine(0).Baseline).Within(1e-3));
    }

    [Test]
    public void ADropCap_StopsBeforeIt()
    {
        const string text = "A￼ upon a time there lived a king whose daughters were all beautiful, but the youngest";
        var layout = Lay(text, new Size<double>(20, 10), width: 300, setUp: set => set.DropCap = new DropCap(3, 2));

        Assert.That(layout.GetInlineObjects().Select(placed => placed.Index), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void JustifiedWithGlyphScaling_ItKeepsItsWidthAndTheTextDoesNotRunIntoIt()
    {
        const string text = "aaa bbb ￼ ccc ddd eee fff ggg hhh iii jjj kkk lll mmm nnn ooo";
        var layout = Lay(text, new Size<double>(30, 10), width: 200, alignment: HorizontalTextAlignment.Justify,
            setUp: set => set.GlyphScaling = new SpacingRange(0.9, 1, 1.1));
        var placed = layout.GetInlineObjects().Single();
        var after = layout.GetTextData().Where(glyph => glyph.PositionInString > placed.Index
                                                         && glyph.LineIndex == 0 && glyph.Symbol != ' ');

        Assert.That(placed.Rect.Width, Is.EqualTo(30).Within(1e-3));
        Assert.That(after.Min(glyph => glyph.Rect.X), Is.GreaterThanOrEqualTo(placed.Rect.Right - 1e-3));
    }

    // Down a vertical line an object stands upright: its height along the line, centered across it, and a line it is
    // wider than grows to hold it. There were none, and a control set there was given no room.
    [Test]
    public void InVerticalText_ItStandsUprightAcrossItsLine()
    {
        var text = "ab" + Object + "cd";
        var plain = Lay("abcd", null, height: 200, setUp: set => set.WritingMode = WritingMode.VerticalRightToLeft);
        var layout = Lay(text, new Size<double>(60, 30), height: 200,
            setUp: set => set.WritingMode = WritingMode.VerticalRightToLeft);
        var placed = layout.GetInlineObjects().Single();
        var line = layout.GetRangeRects(0, text.Length).Single();
        var before = layout.GetRangeRects(1, 2).Single();
        var after = layout.GetRangeRects(3, 4).Single();

        Assert.Multiple(() =>
        {
            Assert.That(placed.Index, Is.EqualTo(2));
            Assert.That((placed.Rect.Width, placed.Rect.Height), Is.EqualTo((60f, 30f)), "upright, as it is");
            Assert.That(placed.Rect.Top, Is.EqualTo(before.Bottom).Within(1), "after b down the line");
            Assert.That(after.Top, Is.EqualTo(placed.Rect.Bottom).Within(1), "and c after it");
            Assert.That(line.Width, Is.GreaterThanOrEqualTo(60 - 1e-3), "the line as wide as it");
            Assert.That(line.Width, Is.GreaterThan(plain.GetRangeRects(0, 4).Single().Width), "grown for it");
            Assert.That(placed.Rect.Left + placed.Rect.Width / 2, Is.EqualTo(line.Left + line.Width / 2).Within(4),
                "centered across it");
        });
    }

    [Test]
    public void NoGlyphIsDrawnForIt()
    {
        var plain = Lay("ab", null);
        var layout = Lay("ab" + Object, new Size<double>(30, 10));

        Assert.That(layout.GetGlyphs().Count(), Is.EqualTo(plain.GetGlyphs().Count()));
    }

    [Test]
    public void WithoutASize_TheCharacterIsText()
    {
        var plain = Lay("ab", null);
        var layout = Lay("ab" + Object, null);

        Assert.That(layout.GetInlineObjects(), Is.Empty);
        Assert.That(layout.GetGlyphs().Count(), Is.GreaterThan(plain.GetGlyphs().Count()));
    }
}
