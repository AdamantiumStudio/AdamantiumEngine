using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Balanced lines, as CSS's text-wrap: balance and a ribbon command's label: the text takes the narrowest width
/// that keeps its lines - MaxLines of them, or as many as wrapping at the full width gives - and the lines are aligned in
/// the full width.</summary>
[TestFixture]
public class BalancedLinesTests
{
    private const double FontSize = 20;

    private static string FontPath(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", name);

    private const string Prose = "Once upon a time there lived a king whose daughters were all beautiful, but the " +
                                 "youngest was so beautiful that the sun itself was astonished whenever it shone.";

    private static TextLayout Lay(string text, Action<TextLayout> setUp = null, double width = double.NaN,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left, TextTrimming trimming = TextTrimming.None,
        double height = double.NaN, AttributedText attributed = null, double fontSize = FontSize)
    {
        var typeface = Typeface.LoadFont(FontPath("SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, Language = "en-US" };
        setUp?.Invoke(layout);
        if (attributed != null)
        {
            layout.ProcessText(attributed, fontSize, new Size(width, height), TextWrapping.WrapByWords, trimming,
                alignment, VerticalTextAlignment.Top);
        }
        else
        {
            layout.ProcessText(text, fontSize, new Size(width, height), TextWrapping.WrapByWords, trimming,
                alignment, VerticalTextAlignment.Top);
        }

        return layout;
    }

    private static string Drawn(TextLayout layout) => new(layout.GetTextData().Select(glyph => glyph.Symbol).ToArray());

    private static string[] Lines(TextLayout layout) => Enumerable.Range(0, layout.LineCount)
        .Select(i => layout.GetLine(i))
        .Select(line => layout.Text[line.Start..line.End].Trim())
        .ToArray();

    private static double Ink(TextLayout layout, int line)
    {
        var metrics = layout.GetLine(line);
        var rects = layout.GetRangeRects(metrics.Start, metrics.Start + layout.Text[metrics.Start..metrics.End].TrimEnd().Length);
        return rects.Max(rect => rect.Right) - rects.Min(rect => rect.Left);
    }

    private static double Widest(TextLayout layout) => Enumerable.Range(0, layout.LineCount).Max(i => Ink(layout, i));

    private static double OneLine(string text) => Ink(Lay(text, width: double.NaN), 0);

    private static void Balanced(TextLayout layout)
    {
        layout.BalanceLines = true;
        layout.MaxLines = 2;
    }

    [Test]
    public void TwoWordsInTwoLines_TakeALineEach()
    {
        var layout = Lay("Format Painter", Balanced);

        Assert.That(Lines(layout), Is.EqualTo(new[] { "Format", "Painter" }));
        Assert.That(layout.CalculatedLayoutSize.Width, Is.LessThan(OneLine("Format Painter")));
    }

    [Test]
    public void ThreeWords_BreakWhereTheWiderLineIsNarrowest()
    {
        var layout = Lay("Insert Table Row", Balanced);

        var best = Math.Min(Math.Max(OneLine("Insert Table"), OneLine("Row")),
            Math.Max(OneLine("Insert"), OneLine("Table Row")));
        Assert.That(layout.LineCount, Is.EqualTo(2));
        Assert.That(Widest(layout), Is.EqualTo(best).Within(1.5));
    }

    [Test]
    public void OneWord_StaysOnOneLine()
    {
        var layout = Lay("Paste", Balanced);

        Assert.That(Lines(layout), Is.EqualTo(new[] { "Paste" }));
    }

    [Test]
    public void ItIsOffByDefault()
    {
        var layout = Lay("Format Painter", set => set.MaxLines = 2);

        Assert.That(layout.LineCount, Is.EqualTo(1));
    }

    [Test]
    public void WithoutMostLines_ItKeepsTheLinesWrappingGives_AndEvensThem()
    {
        // Wrapped greedily the last line holds one word.
        const string text = "Once upon a time there lived a king whose";
        var greedy = Lay(text, width: 300);
        var balanced = Lay(text, set => set.BalanceLines = true, 300);

        Assert.That(Lines(greedy).Last(), Is.EqualTo("whose"));
        Assert.That(balanced.LineCount, Is.EqualTo(greedy.LineCount));
        Assert.That(Widest(balanced), Is.LessThan(Widest(greedy)));
        Assert.That(Ink(balanced, balanced.LineCount - 1), Is.GreaterThan(Ink(greedy, greedy.LineCount - 1)));
    }

    [Test]
    public void TextThatFitsOneLine_IsNotBrokenWithoutMostLines()
    {
        var layout = Lay("Format Painter", set => set.BalanceLines = true, 300);

        Assert.That(layout.LineCount, Is.EqualTo(1));
    }

    [Test]
    public void TheLines_AreAlignedInTheFullWidth()
    {
        var layout = Lay("Format Painter", Balanced, 300, HorizontalTextAlignment.Center);

        Assert.That(layout.LineCount, Is.EqualTo(2));
        Assert.That(layout.CalculatedLayoutSize.Width, Is.EqualTo(300));
        for (var i = 0; i < 2; i++)
        {
            var line = layout.GetLine(i);
            var rects = layout.GetRangeRects(line.Start, line.Start + layout.Text[line.Start..line.End].TrimEnd().Length);
            Assert.That((rects.Min(rect => rect.Left) + rects.Max(rect => rect.Right)) / 2, Is.EqualTo(150).Within(1.5),
                $"line {i}");
        }
    }

    [TestCase(TextTrimming.CharEllipses)]
    [TestCase(TextTrimming.WordEllipses)]
    public void TrimmedText_IsBalancedWhole_NotCutToEllipses(TextTrimming trimming)
    {
        var layout = Lay("Format Painter", Balanced, trimming: trimming);

        Assert.That(Lines(layout), Is.EqualTo(new[] { "Format", "Painter" }));
        Assert.That(Drawn(layout), Does.Not.Contain("…").And.Not.Contain("..."));
    }

    // A width is tried as the layout measures a word, so no word lands a fraction of a pixel past it at any size.
    [TestCase("Format Painter")]
    [TestCase("Header Footer")]
    [TestCase("Clear All")]
    public void AtAnySize_TrimmedLabels_AreBalancedWhole(string label)
    {
        for (var size = 12.0; size <= 30; size += 0.25)
        {
            var layout = Lay(label, Balanced, trimming: TextTrimming.CharEllipses, fontSize: size);

            Assert.That(Drawn(layout), Does.Not.Contain("…").And.Not.Contain("..."), $"at {size}");
            Assert.That(layout.LineCount, Is.EqualTo(2), $"at {size}");
        }
    }

    [TestCase(20)]
    [TestCase(40)]
    public void InAnUnboundedArea_ALargeFontIsBalancedToo(double size)
    {
        var layout = Lay("Insert Column", Balanced, fontSize: size);

        Assert.That(Lines(layout), Is.EqualTo(new[] { "Insert", "Column" }));
    }

    [Test]
    public void JustifiedText_IsBalancedToo()
    {
        const string text = "Once upon a time there lived a king whose";
        var greedy = Lay(text, width: 300, alignment: HorizontalTextAlignment.Justify);
        var balanced = Lay(text, set => set.BalanceLines = true, 300, HorizontalTextAlignment.Justify);

        Assert.That(Lines(greedy).Last(), Is.EqualTo("whose"));
        Assert.That(Lines(balanced).Last(), Is.Not.EqualTo("whose"));
    }

    [Test]
    public void ASoftHyphen_DoesNotBreakOneWord()
    {
        var layout = Lay("Auto­Format", Balanced);

        Assert.That(layout.LineCount, Is.EqualTo(1));
    }

    [Test]
    public void LinesTheHeightHasNoRoomFor_AreNotMade()
    {
        var oneLine = Lay("x").GetLine(0).Height + 2;
        var plain = Lay("Format Painter", set => set.MaxLines = 2, 300, trimming: TextTrimming.CharEllipses,
            height: oneLine);
        var balanced = Lay("Format Painter", Balanced, 300, trimming: TextTrimming.CharEllipses, height: oneLine);

        Assert.That(Lines(plain), Is.EqualTo(new[] { "Format Painter" }));
        Assert.That(Lines(balanced), Is.EqualTo(Lines(plain)));
        Assert.That(Drawn(balanced), Is.EqualTo(Drawn(plain)));
    }

    [Test]
    public void AParagraphsMargins_DoNotTurnItOff()
    {
        const string text = "Once upon a time there lived a king whose";
        var attributed = new AttributedText(text);
        attributed.SetParagraph(0, text.Length, new ParagraphAttributes { MarginLeft = 30, MarginRight = 10 });
        var greedy = Lay(text, width: 340, attributed: attributed);
        var balanced = Lay(text, set => set.BalanceLines = true, 340, attributed: attributed);

        Assert.That(Lines(greedy).Last(), Is.EqualTo("whose"));
        Assert.That(balanced.LineCount, Is.EqualTo(greedy.LineCount));
        Assert.That(Lines(balanced).Last(), Is.Not.EqualTo("whose"));
    }

    [TestCase("Extraordinary")]
    [TestCase("Conditional")]
    public void WithHyphenation_OneWord_StaysWhole(string word)
    {
        var layout = Lay(word, set =>
        {
            Balanced(set);
            set.Hyphens = Hyphens.Auto;
        });

        Assert.That(Lines(layout), Is.EqualTo(new[] { word }));
    }

    [Test]
    public void WithHyphenation_WordsBreakBetweenThemselves()
    {
        var layout = Lay("Insert Table Row", set =>
        {
            Balanced(set);
            set.Hyphens = Hyphens.Auto;
        });

        Assert.That(layout.LineCount, Is.EqualTo(2));
        Assert.That(Drawn(layout), Does.Not.Contain("-"));
    }

    [Test]
    public void LongText_IsLaidAsWithoutBalance()
    {
        var greedy = Lay(Prose + " " + Prose + " " + Prose, width: 150);
        var balanced = Lay(Prose + " " + Prose + " " + Prose, set => set.BalanceLines = true, 150);

        Assert.That(greedy.LineCount, Is.GreaterThan(10));
        Assert.That(Lines(balanced), Is.EqualTo(Lines(greedy)));
    }

    [Test]
    public void TextTooLongForTheMostLines_IsLaidAsWithoutBalance()
    {
        const string text = "Once upon a time there lived a king whose daughters were all beautiful";
        var plain = Lay(text, set => set.MaxLines = 2, 200);
        var balanced = Lay(text, Balanced, 200);

        Assert.That(Lines(balanced), Is.EqualTo(Lines(plain)));
        Assert.That(balanced.OversetIndex, Is.EqualTo(plain.OversetIndex));
    }
}
