using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Optical margin alignment: punctuation and hyphens at the edges of lines hang partly past the margin.</summary>
[TestFixture]
public class OpticalMarginTests
{
    private const double FontSize = 20;
    private const double Width = 300;

    private static readonly Lazy<Typeface> SourceSans = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf")));

    private static TextLayout Layout(string text, HorizontalTextAlignment alignment, bool optical = true,
        TextDirection direction = TextDirection.Auto, double width = Width)
    {
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null,
            OpticalMarginAlignment = optical,
            Direction = direction,
        };
        layout.ProcessText(text, FontSize, new Size<double>(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            alignment, VerticalTextAlignment.Top);
        return layout;
    }

    private static GlyphWordData At(TextLayout layout, int index) =>
        layout.GetTextData().Single(g => g.PositionInString == index);

    [Test]
    public void AnOpeningQuote_HangsPastTheLeftMargin()
    {
        var hanging = At(Layout("“Quoted,” she said.", HorizontalTextAlignment.Left), 0);
        var plain = At(Layout("“Quoted,” she said.", HorizontalTextAlignment.Left, false), 0);

        Assert.That(plain.PenX, Is.Zero);
        Assert.That(hanging.PenX, Is.EqualTo(-0.7 * hanging.Advance).Within(1e-3));
    }

    [Test]
    public void APeriod_HangsPastTheRightMargin()
    {
        const string text = "It ends here.";
        var layout = Layout(text, HorizontalTextAlignment.Right);
        var period = At(layout, text.Length - 1);

        Assert.That(period.PenX + period.Advance, Is.GreaterThan(Width));
    }

    [Test]
    public void AJustifiedLine_HangsBothEnds()
    {
        const string text = "“The first line of this paragraph is long enough to wrap, the second is not.”";
        var comma = text.IndexOf(',');
        var unwrapped = At(Layout(text, HorizontalTextAlignment.Left, false, width: double.NaN), comma);
        var width = Math.Ceiling(unwrapped.PenX + unwrapped.Advance + 10);
        var layout = Layout(text, HorizontalTextAlignment.Justify, width: width);
        var first = layout.GetTextData().Where(g => g.LineIndex == 0 && g.Symbol != ' ').OrderBy(g => g.PenX).ToArray();
        var last = first[^1];

        Assert.That(last.PositionInString, Is.EqualTo(comma), "the first line ends at the comma");
        Assert.That(first[0].PenX, Is.LessThan(0), "the quote hangs left");
        Assert.That(last.Rect.Right, Is.EqualTo(width + 0.7 * last.Advance).Within(0.5));
    }

    [Test]
    public void TheTrimmingEllipsis_DoesNotHang()
    {
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, OpticalMarginAlignment = true };
        layout.ProcessText("A line much too long for the width it is given here", FontSize, new Size<double>(150, double.NaN),
            TextWrapping.NoWrap, TextTrimming.CharEllipses, HorizontalTextAlignment.Right, VerticalTextAlignment.Top);

        Assert.That(layout.GetTextData().Max(g => g.Rect.Right), Is.LessThanOrEqualTo(150.5));
    }

    [Test]
    public void ARightToLeftLine_DoesNotHang()
    {
        var layout = Layout("שלום.", HorizontalTextAlignment.Left, true, TextDirection.RightToLeft);
        var plain = Layout("שלום.", HorizontalTextAlignment.Left, false, TextDirection.RightToLeft);

        Assert.That(layout.GetTextData().Select(g => g.PenX), Is.EqualTo(plain.GetTextData().Select(g => g.PenX)));
    }
}
