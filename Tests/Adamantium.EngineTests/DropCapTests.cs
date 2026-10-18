using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Drop caps: the first characters set across the first lines, from the cap height of the first to the
/// baseline of the last, the lines beside them indented by their width.</summary>
[TestFixture]
public class DropCapTests
{
    private const double FontSize = 20;
    private const double Width = 300;

    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone " +
        "in her face.";

    private static readonly Lazy<Typeface> SourceSans = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf")));

    private static readonly Lazy<Typeface> SourceSansBold = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Bold.ttf")));

    private static TextLayout Layout(string text, DropCap dropCap, LineBreaking breaking = LineBreaking.Greedy,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left)
    {
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, DropCap = dropCap, LineBreaking = breaking };
        layout.ProcessText(text, FontSize, new Size(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            alignment, VerticalTextAlignment.Top);
        return layout;
    }

    private static GlyphWordData[] Body(TextLayout layout) =>
        layout.GetTextData().Where(g => g.PositionInString > 0).ToArray();

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void TheDropCap_SpansFromTheCapHeightToTheBaselineOfItsLastLine(int lines)
    {
        var layout = Layout(Prose, new DropCap(lines));
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var font = SourceSans.Value.Fonts[0];
        var firstBaseline = layout.GetLine(0).Baseline;
        var capTop = firstBaseline - font.CapsHeight * FontSize / font.UnitsPerEm;

        Assert.That(cap.FontSize, Is.GreaterThan(FontSize * (lines - 0.5)));
        Assert.That(cap.Rect.Top, Is.EqualTo(capTop).Within(1.5), "its top at the cap height of the first line");
        Assert.That(cap.Rect.Bottom, Is.EqualTo(layout.GetLine(lines - 1).Baseline).Within(1.5),
            "its foot on the baseline of its last line");
    }

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void TheLinesBesideIt_AreIndentedByItsWidth(LineBreaking breaking)
    {
        var layout = Layout(Prose, new DropCap(3), breaking);
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var body = Body(layout);

        for (var line = 0; line < 3; line++)
        {
            Assert.That(body.Where(g => g.LineIndex == line).Min(g => g.PenX), Is.GreaterThanOrEqualTo(cap.PenX + cap.Advance - 1e-3),
                $"line {line}");
        }

        Assert.That(body.Where(g => g.LineIndex == 3).Min(g => g.PenX), Is.Zero, "the fourth line starts at the margin");
        Assert.That(body.Where(g => g.Symbol != ' ').Max(g => g.Rect.Right), Is.LessThanOrEqualTo(Width + 0.5));
    }

    [Test]
    public void AJustifiedLineBesideIt_FillsTheRestOfTheWidth()
    {
        var layout = Layout(Prose, new DropCap(3), LineBreaking.Paragraph, HorizontalTextAlignment.Justify);
        var body = Body(layout);

        Assert.That(body.Where(g => g.LineIndex == 1 && g.Symbol != ' ').Max(g => g.Rect.Right), Is.EqualTo(Width).Within(0.5));
    }

    [Test]
    public void AShortText_StillMakesRoomForTheWholeDropCap()
    {
        var plain = Layout("In short.", null);
        var dropped = Layout("In short.", new DropCap(3));

        Assert.That(dropped.CalculatedLayoutSize.Height, Is.GreaterThanOrEqualTo(3 * plain.GetLine(0).Height - 1));
    }

    [Test]
    public void ADecorativeFont_SetsTheDropCap()
    {
        var bold = SourceSansBold.Value.Fonts[0];
        var layout = Layout(Prose, new DropCap(3, 1, bold));

        Assert.That(layout.GetTextData().Single(g => g.PositionInString == 0).Font, Is.SameAs(bold));
        Assert.That(Body(layout).All(g => !ReferenceEquals(g.Font, bold)));
    }

    [Test]
    public void SeveralCharacters_MakeOneDropCap()
    {
        var layout = Layout(Prose, new DropCap(2, 2));
        var caps = layout.GetTextData().Where(g => g.PositionInString is 0 or 1).ToArray();

        Assert.That(caps.Select(g => g.FontSize).Distinct().Count(), Is.EqualTo(1));
        Assert.That(caps.All(g => g.FontSize > FontSize));
    }

    [Test]
    public void TheCaretBeforeTheFirstCharacter_StandsAtTheDropCap()
    {
        var layout = Layout(Prose, new DropCap(3));
        var stops = layout.GetCaretStops();

        Assert.That(stops[0].LineIndex, Is.Zero);
        Assert.That(stops[1].X, Is.GreaterThan(stops[0].X));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void AClickOnTheDropCap_FindsIt_OnEveryLineItSpans(int line)
    {
        var layout = Layout(Prose, new DropCap(3));
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var y = layout.GetLine(line).Top + layout.GetLine(line).Height / 2;
        var leading = layout.HitTest(cap.PenX + cap.Advance * 0.25, y);
        var trailing = layout.HitTest(cap.PenX + cap.Advance * 0.75, y);
        var beside = layout.HitTest(cap.PenX + cap.Advance + 0.5, y);

        Assert.That((leading.Index, leading.CaretIndex, leading.IsInside), Is.EqualTo((0, 0, true)), "its left half");
        Assert.That((trailing.Index, trailing.CaretIndex, trailing.IsInside), Is.EqualTo((0, 1, true)), "its right half");
        Assert.That(beside.CaretIndex, Is.EqualTo(Body(layout).Where(g => g.LineIndex == line).Min(g => g.PositionInString)),
            "past it, the start of the line beside it");
    }

    [Test]
    public void AClickBelowAShortText_IsNotOnTheDropCap()
    {
        var layout = Layout("Ab cd", new DropCap(3));
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);

        Assert.That(layout.HitTest(cap.PenX + cap.Advance / 2, 1000).IsInside, Is.False);
    }

    [TestCase("A")]
    [TestCase("A\n")]
    [TestCase("A\n\n")]
    public void ATextThatIsOnlyTheDropCap_IsLaidOut(string text)
    {
        var layout = Layout(text, new DropCap(3));

        Assert.That(layout.GetTextData().Single(g => g.PositionInString == 0).FontSize, Is.GreaterThan(FontSize));
    }

    [Test]
    public void ALigatureAcrossTheDropCap_KeepsEveryCharacter()
    {
        const string text = "find official waffles in a fine field of fireflies";
        var playfair = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "PlayfairDisplay-Regular.ttf"));
        var layout = new TextLayout(playfair, playfair.Fonts[0]) { Fallback = null, DropCap = new DropCap(2) };
        layout.ProcessText(text, FontSize, new Size(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var plain = new TextLayout(playfair, playfair.Fonts[0]) { Fallback = null };
        plain.ProcessText(text, FontSize, new Size(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var clusters = layout.GetTextData().Where(g => g.PositionInString >= 0).Select(g => g.PositionInString).ToHashSet();

        Assert.That(plain.GetTextData().Any(g => g.PositionInString == 1), Is.False, "the font joins f and i");
        Assert.That(clusters, Does.Contain(1), "the i after the dropped f");
    }

    [Test]
    public void ALeadingTab_IsNoDropCap()
    {
        var layout = Layout("\tIndented text", new DropCap(3));

        Assert.That(layout.GetTextData().All(g => g.FontSize <= FontSize));
    }

    [Test]
    public void AFontWithoutTheLetter_LeavesTheDropCapToTheTextsFont()
    {
        var hebrew = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf")).Fonts[0];
        var layout = Layout(Prose, new DropCap(3, 1, hebrew));

        Assert.That(layout.GetTextData().Single(g => g.PositionInString == 0).Font, Is.Not.SameAs(hebrew));
    }

    [Test]
    public void TheLinesBesideIt_StartPastItsInk()
    {
        var layout = Layout("fortune favors the bold, and the bold write long sentences that wrap over lines", new DropCap(3));
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);

        Assert.That(Body(layout).Where(g => g.LineIndex < 3).Min(g => g.Rect.Left), Is.GreaterThan(cap.Rect.Right));
    }

    [Test]
    public void AtTheBottom_TheDropCapStaysInTheBox()
    {
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, DropCap = new DropCap(3) };
        layout.ProcessText("Ab cd", FontSize, new Size(Width, 200), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Bottom);

        Assert.That(layout.GetTextData().Single(g => g.PositionInString == 0).Rect.Bottom, Is.LessThanOrEqualTo(200.5));
    }

    [Test]
    public void ATallerLine_StillTakesTheFootOfTheDropCap()
    {
        var typeface = SourceSans.Value;
        var text = new AttributedText(Prose).Apply(Prose.IndexOf("beautiful", StringComparison.Ordinal), 9,
            new TextAttributes { FontSize = 40 });
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, DropCap = new DropCap(3) };
        layout.ProcessText(text, FontSize, new Size(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetTextData().Single(g => g.PositionInString == 0).Rect.Bottom,
            Is.EqualTo(layout.GetLine(2).Baseline).Within(1.5));
    }

    [TestCase(1, 1)]
    [TestCase(3, 0)]
    public void ADropCapOfNoLinesOrCharacters_IsRejected(int lines, int characters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new DropCap(lines, characters));
    }
}
