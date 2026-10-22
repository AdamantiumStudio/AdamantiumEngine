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

    private const string HebrewProse =
        "בראשית ברא אלהים את השמים ואת הארץ והארץ היתה תהו ובהו וחשך על פני תהום ורוח אלהים מרחפת על פני המים " +
        "ויאמר אלהים יהי אור ויהי אור וירא אלהים את האור כי טוב ויבדל אלהים בין האור ובין החשך";

    private static readonly Lazy<Typeface> Hebrew = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf")));

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

    // A paragraph running right to left, in Hebrew.
    private static TextLayout RightToLeft(AttributedText text, DropCap dropCap, LineBreaking breaking = LineBreaking.Greedy,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left)
    {
        var typeface = Hebrew.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, DropCap = dropCap, LineBreaking = breaking, Direction = TextDirection.RightToLeft,
        };
        layout.ProcessText(text, FontSize, new Size(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            alignment, VerticalTextAlignment.Top);
        return layout;
    }

    private static TextLayout RightToLeft(string text, DropCap dropCap, LineBreaking breaking = LineBreaking.Greedy,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left) =>
        RightToLeft(new AttributedText(text), dropCap, breaking, alignment);

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

    // Sized by a letter of its script in its font - H, ה, alef, 国 - not by its own: a lowercase letter is as large as a
    // capital, a letter of short ink is not blown up, and a Hebrew cap stands as high as the Hebrew letters beside it.
    [Test]
    public void AHebrewDropCap_StandsAsHighAsTheLettersOfItsFirstLine()
    {
        var layout = RightToLeft("ש " + HebrewProse, new DropCap(3));
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var letters = Body(layout).Where(g => g.LineIndex == 0 && "בראשית".Contains(g.Symbol)).Min(g => g.Rect.Top);

        Assert.That(cap.Rect.Top, Is.EqualTo(letters).Within(1.5));
    }

    // An alef cap stands as high as the alefs of its first line, and a cap of a short letter is as large; a Syriac one, a
    // script with no letter to go by, stands by its own.
    [TestCase("NotoSansArabic-Regular.ttf", "ا", "ب", "بسم الله الرحمن الرحيم والحمد لله رب العالمين الرحمن الرحيم مالك يوم الدين")]
    [TestCase("NotoSansSyriac-Regular.ttf", "ܐ", "ܐ", "ܐܢܐ ܐܝܬܝ ܐܠܦܐ ܘܐܢܐ ܐܝܬܝ ܬܘ ܫܘܪܝܐ ܘܫܘܠܡܐ ܐܡܪ ܡܪܝܐ ܐܠܗܐ")]
    public void ADropCapOfAScriptWithoutCapitals_StandsAsHighAsItsFirstLine(string font, string tallLetter,
        string shortLetter, string text)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", font));
        GlyphWordData[] Cap(string letter, out double tall)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, DropCap = new DropCap(3), Direction = TextDirection.RightToLeft,
            };
            layout.ProcessText(letter + " " + text, FontSize, new Size(Width, double.NaN), TextWrapping.WrapByWords,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            tall = Body(layout).Where(g => g.LineIndex == 0 && tallLetter[0] == g.Symbol).Min(g => g.Rect.Top);
            return layout.GetTextData().Where(g => g.PositionInString == 0).ToArray();
        }

        var tallCap = Cap(tallLetter, out var tallTop);
        var shortCap = Cap(shortLetter, out _);

        Assert.That(tallCap.Max(g => g.FontSize), Is.GreaterThan(FontSize * 2));
        Assert.That(tallCap.Min(g => g.Rect.Top), Is.EqualTo(tallTop).Within(1.5), "as high as its first line");
        Assert.That(shortCap.Max(g => g.FontSize), Is.EqualTo(tallCap.Max(g => g.FontSize)).Within(1e-6));
    }

    // A letter with its accent is sized as the letter, composed or not.
    [Test]
    public void AnAccentedDropCap_IsSizedAsItsLetter_ComposedOrNot()
    {
        double CapSize(string start) =>
            Layout(start + "clair " + Prose, new DropCap(3)).GetTextData().Where(g => g.PositionInString == 0).Max(g => g.FontSize);

        var composed = CapSize(((char)0xC9).ToString());
        var decomposed = CapSize("E" + (char)0x301);

        Assert.That(composed, Is.EqualTo(CapSize("E")).Within(1e-6));
        Assert.That(decomposed, Is.EqualTo(composed).Within(1e-6));
    }

    // Set in a font of Hangul beside text with none, every syllable is sized alike, by the syllable, not by its first
    // jamo.
    [TestCase("가")]
    [TestCase("이")]
    public void AHangulDropCapInAFontOfItsOwn_IsSizedAsEverySyllable(string syllable)
    {
        var cjk = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansCJK-Regular.ttc")).Fonts[0];
        double CapSize(string start) =>
            Layout(start + "국어 " + Prose, new DropCap(3, 1, cjk)).GetTextData().Where(g => g.PositionInString == 0)
                .Max(g => g.FontSize);

        Assert.That(CapSize(syllable), Is.EqualTo(CapSize("한")).Within(1).Percent);
    }

    // A letter beyond the basic plane and a halfwidth one are sized as the rest of their script.
    [TestCase("𠮷", "吉")]
    [TestCase("ｱ", "ア")]
    public void ACjkDropCap_IsSizedAsTheRestOfItsScript(string letter, string other)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansCJK-Regular.ttc"));
        double CapSize(string cap)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, DropCap = new DropCap(3) };
            layout.ProcessText(cap + string.Concat(Enumerable.Repeat("漢字仮名交じり文", 10)), FontSize,
                new Size(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Left,
                VerticalTextAlignment.Top);
            return layout.GetTextData().Single(g => g.PositionInString == 0).FontSize;
        }

        Assert.That(CapSize(letter), Is.EqualTo(CapSize(other)).Within(1e-6));
    }

    [Test]
    public void ADropCap_IsSizedByALetterOfItsScript_NotByItsOwn()
    {
        var lower = Layout("and " + Prose, new DropCap(3)).GetTextData().Single(g => g.PositionInString == 0);
        var upper = Layout("And " + Prose, new DropCap(3)).GetTextData().Single(g => g.PositionInString == 0);
        var low = RightToLeft("נ " + HebrewProse, new DropCap(3)).GetTextData().Single(g => g.PositionInString == 0);
        var tall = RightToLeft("ל " + HebrewProse, new DropCap(3)).GetTextData().Single(g => g.PositionInString == 0);

        Assert.Multiple(() =>
        {
            Assert.That(lower.FontSize, Is.EqualTo(upper.FontSize).Within(1e-6));
            Assert.That(low.FontSize, Is.EqualTo(tall.FontSize).Within(1e-6));
        });
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

    // Centered lines stand away from the cap: a click between the two on the first line was taken as past its end.
    [TestCase(0, 0.1)]
    [TestCase(0, 0.9)]
    [TestCase(1, 0.5)]
    [TestCase(2, 0.5)]
    public void AClickBetweenTheDropCapAndTheText_GoesToTheNearerOfThem(int line, double across)
    {
        var layout = Layout(Prose, new DropCap(3), alignment: HorizontalTextAlignment.Center);
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var body = Body(layout).Where(g => g.LineIndex == line).ToArray();
        var capRight = cap.PenX + cap.Advance;
        var bodyLeft = body.Min(g => g.PenX);
        var y = layout.GetLine(line).Top + layout.GetLine(line).Height / 2;

        var hit = layout.HitTest(capRight + (bodyLeft - capRight) * across, y);

        Assert.That(bodyLeft - capRight, Is.GreaterThan(1), "the text stands away from the cap");
        Assert.That((hit.CaretIndex, hit.IsInside), Is.EqualTo((body.Min(g => g.PositionInString), false)),
            "where the text beside the cap starts");
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

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetTextData().All(g => g.FontSize <= FontSize));
            Assert.That(layout.DropCapLength, Is.Zero);
            Assert.That(new DropCap(3).Length("\tIndented text"), Is.Zero);
        });
    }

    [TestCase("Text", 1, 1)]
    [TestCase("Text", 2, 2)]
    [TestCase("“Quoted", 1, 2)]
    [TestCase("A\nB", 3, 1)]
    [TestCase(" Spaced", 1, 0)]
    [TestCase("", 1, 0)]
    public void TheLength_IsWhatTheLayoutTakes(string text, int characters, int length)
    {
        var cap = new DropCap(3, characters);

        Assert.That(cap.Length(text), Is.EqualTo(length));
        if (text.Length > 0)
        {
            Assert.That(Layout(text, cap).DropCapLength, Is.EqualTo(length));
        }
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

    // Selected, the drop cap is covered as far as it stands, across its lines; the text beside it as high as its line.
    [Test]
    public void TheDropCapSelected_IsCoveredAcrossItsLines()
    {
        var layout = Layout(Prose, new DropCap(3));
        var rects = layout.GetRangeRects(0, 4);
        var bottom = layout.GetLine(2).Top + layout.GetLine(2).Height;

        Assert.Multiple(() =>
        {
            Assert.That(rects[0].Top, Is.EqualTo(layout.GetLine(0).Top).Within(0.01));
            Assert.That(rects[0].Bottom, Is.EqualTo(bottom).Within(0.01), "down to its last line");
            Assert.That(rects[1].Height, Is.EqualTo(layout.GetLine(0).Height).Within(0.01), "the text beside it");
        });
    }

    // Before the drop cap and after it, the caret is as tall as it; beside it in the text, as its line.
    [Test]
    public void TheCaretAtTheDropCap_IsAsTallAsIt()
    {
        var layout = Layout(Prose, new DropCap(3));
        var tall = layout.GetLine(2).Top + layout.GetLine(2).Height - layout.GetLine(0).Top;

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetCaretRect(new CaretPosition(0), 1).Height, Is.EqualTo(tall).Within(0.01), "before it");
            Assert.That(layout.GetCaretRect(new CaretPosition(1, true), 1).Height, Is.EqualTo(tall).Within(0.01),
                "after it");
            Assert.That(layout.GetCaretRect(new CaretPosition(1), 1).Height,
                Is.EqualTo(layout.GetLine(0).Height).Within(0.01), "before the text beside it");
        });
    }

    // A drop cap's line of the text beside it is not drawn through it: a squiggle under a misspelled first word starts
    // past the cap; a background covers the cap across its lines.
    [Test]
    public void TheDropCapTakesNoLineOfTheText_AndItsBackgroundAcrossItsLines()
    {
        var typeface = SourceSans.Value;
        var text = new AttributedText(Prose)
            .Apply(0, 6, new TextAttributes { Decorations = TextDecorations.Squiggle })
            .Apply(0, 1, new TextAttributes { Background = Colors.Gold });
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, DropCap = new DropCap(3) };
        layout.ProcessText(text, FontSize, new Size(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var adornments = layout.GetAdornments();

        Assert.Multiple(() =>
        {
            Assert.That(adornments.Where(a => a.Kind == TextAdornmentKind.Squiggle).Min(a => a.Rect.Left),
                Is.GreaterThanOrEqualTo(cap.PenX + cap.Advance - 0.5));
            Assert.That(adornments.Single(a => a.Kind == TextAdornmentKind.Background).Rect.Bottom,
                Is.EqualTo(layout.GetLine(2).Top + layout.GetLine(2).Height).Within(0.01));
        });
    }

    // A text shorter than the drop cap still has its room: the caret at the cap is as tall as the cap's lines.
    [Test]
    public void TheCaretAtTheDropCapOfAShortText_IsAsTallAsItsLines()
    {
        var layout = Layout("In short.", new DropCap(3));

        Assert.That(layout.GetCaretRect(new CaretPosition(0), 1).Height,
            Is.GreaterThan(layout.GetLine(0).Height * 2.5));
    }

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void InARightToLeftParagraph_TheDropCapStandsAtTheRight_AndTheLinesBesideItEndPastItsInk(LineBreaking breaking)
    {
        var layout = RightToLeft(HebrewProse, new DropCap(3), breaking);
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var body = Body(layout).Where(g => g.Symbol != ' ').ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(cap.PenX + cap.Advance, Is.EqualTo(Width).Within(0.5), "the cap ends at the right edge");
            Assert.That(cap.FontSize, Is.GreaterThan(FontSize * 2));
            for (var line = 0; line < 3; line++)
            {
                Assert.That(body.Where(g => g.LineIndex == line).Max(g => g.Rect.Right), Is.LessThan(cap.Rect.Left),
                    $"line {line}");
            }

            Assert.That(body.Where(g => g.LineIndex == 3).Max(g => g.Rect.Right), Is.GreaterThan(Width - 3),
                "the fourth line starts at the margin");
            Assert.That(body.Min(g => g.Rect.Left), Is.GreaterThanOrEqualTo(-0.5));
        });
    }

    [Test]
    public void InARightToLeftParagraph_TheDropCapStandsAtTheMarginItsLinesStartFrom_InPlaceOfTheIndent()
    {
        var text = new AttributedText(HebrewProse).SetParagraph(0, HebrewProse.Length,
            new ParagraphAttributes { MarginRight = 50, TextIndent = 25 });
        var layout = RightToLeft(text, new DropCap(3));
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var besideCap = Enumerable.Range(0, 2).Select(line => Body(layout)
            .Where(g => g.LineIndex == line && g.Symbol != ' ').Max(g => (double)g.Rect.Right)).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(cap.PenX + cap.Advance, Is.EqualTo(Width - 50).Within(0.5));
            Assert.That(besideCap[0], Is.EqualTo(besideCap[1]).Within(3), "no indent beside the cap");
            Assert.That(besideCap[0], Is.LessThan(cap.Rect.Left));
        });
    }

    [Test]
    public void InARightToLeftParagraph_AJustifiedLineBesideTheDropCap_FillsTheRestOfTheWidth()
    {
        var layout = RightToLeft(HebrewProse, new DropCap(3), LineBreaking.Paragraph, HorizontalTextAlignment.Justify);
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var line = Body(layout).Where(g => g.LineIndex == 1 && g.Symbol != ' ').ToArray();

        Assert.That(line.Min(g => g.Rect.Left), Is.EqualTo(0).Within(1));
        Assert.That(line.Max(g => g.Rect.Right), Is.LessThan(cap.Rect.Left));
    }

    // Laid out as wide as it needs, the text beside the cap keeps out of it and inside the box.
    [Test]
    public void InARightToLeftParagraphAsWideAsItsText_TheDropCapStandsInTheBox()
    {
        var typeface = Hebrew.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, DropCap = new DropCap(2), Direction = TextDirection.RightToLeft,
        };
        layout.ProcessText("שלום עולם\nטוב", FontSize, new Size(double.NaN, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var body = Body(layout).Where(g => g.Symbol != ' ').ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(cap.FontSize, Is.GreaterThan(FontSize));
            Assert.That(cap.PenX + cap.Advance, Is.EqualTo(layout.CalculatedLayoutSize.Width).Within(1));
            Assert.That(body.Min(g => g.Rect.Left), Is.GreaterThanOrEqualTo(-0.5));
            Assert.That(body.Where(g => g.LineIndex == 0).Max(g => g.Rect.Right), Is.LessThan(cap.Rect.Left));
        });
    }

    [Test]
    public void SeveralCharacters_OfARightToLeftDropCap_StandInVisualOrder()
    {
        var layout = RightToLeft(HebrewProse, new DropCap(2, 2));
        var first = layout.GetTextData().Single(g => g.PositionInString == 0);
        var second = layout.GetTextData().Single(g => g.PositionInString == 1);

        Assert.That(first.FontSize, Is.EqualTo(second.FontSize));
        Assert.That(first.PenX, Is.GreaterThan(second.PenX), "the first at the right");
    }

    // Characters of a drop cap that run each their own way stand as in their line: the bracket opening a right-to-left
    // paragraph mirrored at the right, the number left to right beside it.
    [Test]
    public void AMixedDropCap_StandsAsItsLineWouldShowIt()
    {
        const string text = "(1) In olden times when wishing still helped one, there lived a king whose daughters were all beautiful";
        var typeface = SourceSans.Value;
        TextLayout Lay(DropCap dropCap)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, DropCap = dropCap, Direction = TextDirection.RightToLeft,
            };
            layout.ProcessText(text, FontSize, new Size(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout;
        }

        var capped = Lay(new DropCap(3));
        var plain = Lay(null);
        var bracket = capped.GetTextData().Single(g => g.PositionInString == 0);
        var number = capped.GetTextData().Single(g => g.PositionInString == 1);

        Assert.Multiple(() =>
        {
            Assert.That(bracket.PenX, Is.GreaterThan(number.PenX), "the bracket at the right");
            Assert.That(bracket.Glyph.Index, Is.EqualTo(plain.GetTextData().Single(g => g.PositionInString == 0).Glyph.Index),
                "mirrored, as in the line");
            Assert.That(capped.GetCaretPoint(new CaretPosition(0)).X, Is.EqualTo(bracket.PenX + bracket.Advance).Within(0.5));
            Assert.That(capped.GetCaretStops()[1].X, Is.EqualTo(number.PenX).Within(0.5), "before the number, at its left");
        });
    }

    // Each character of a right-to-left drop cap has its own caret stop, selection and click.
    [Test]
    public void EachCharacterOfARightToLeftDropCap_IsSelectedAndClickedAlone()
    {
        var layout = RightToLeft(HebrewProse, new DropCap(3, 2));
        var second = layout.GetTextData().Single(g => g.PositionInString == 1);
        var rects = layout.GetRangeRects(1, 2);
        var y = layout.GetLine(1).Top + layout.GetLine(1).Height / 2;
        var hit = layout.HitTest(second.PenX + second.Advance / 2, y);

        Assert.Multiple(() =>
        {
            Assert.That(rects.Min(r => r.Left), Is.EqualTo(second.PenX).Within(0.5));
            Assert.That(rects.Max(r => r.Right), Is.EqualTo(second.PenX + second.Advance).Within(0.5));
            Assert.That((hit.Index, hit.IsInside), Is.EqualTo((1, true)));
        });
    }

    // Two scripts running right to left in one cap stand as their line shows them: the first at the right.
    [Test]
    public void ADropCapOfTwoRightToLeftScripts_StandsAsItsLineShowsIt()
    {
        var layout = RightToLeft("אب " + HebrewProse, new DropCap(3, 2));
        var first = layout.GetTextData().Single(g => g.PositionInString == 0);
        var second = layout.GetTextData().Single(g => g.PositionInString == 1);

        Assert.That(first.PenX, Is.GreaterThan(second.PenX));
        Assert.That(layout.GetCaretPoint(new CaretPosition(0)).X, Is.EqualTo(first.PenX + first.Advance).Within(0.5));
    }

    // The space after a right-to-left cap stands beside it, at the start of its line, as after a left-to-right one.
    [Test]
    public void TheSpaceAfterARightToLeftDropCap_StandsBesideIt()
    {
        var layout = RightToLeft("ב " + HebrewProse, new DropCap(3));
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var space = layout.GetTextData().Single(g => g.PositionInString == 1);

        Assert.That(space.PenX + space.Advance, Is.EqualTo(cap.PenX + cap.Advance - LayoutIndent(layout)).Within(0.5));
        Assert.That(layout.GetCaretPoint(new CaretPosition(1)).X, Is.LessThanOrEqualTo(cap.Rect.Left));
    }

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void TheSpaceAfterARightToLeftDropCap_StandsBesideIt_InAJustifiedParagraph(LineBreaking breaking)
    {
        var layout = RightToLeft("ב " + HebrewProse, new DropCap(3), breaking, HorizontalTextAlignment.Justify);
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var space = layout.GetTextData().Single(g => g.PositionInString == 1);

        Assert.That(space.PenX + space.Advance, Is.LessThanOrEqualTo(cap.Rect.Left));
        Assert.That(Body(layout).Where(g => g.LineIndex == 0 && g.Symbol != ' ').Max(g => g.Rect.Right),
            Is.LessThanOrEqualTo(space.PenX + 0.5));
    }

    // Down vertical lines a cap's characters stand in the order they are read, whatever their direction.
    [Test]
    public void AVerticalDropCapOfRightToLeftLetters_HasItsCaretsInOrder()
    {
        var typeface = Hebrew.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, DropCap = new DropCap(2, 2), Direction = TextDirection.LeftToRight,
            WritingMode = WritingMode.VerticalRightToLeft,
        };
        layout.ProcessText("אב " + Prose, FontSize, new Size(Width, Width), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var stops = layout.GetCaretStops();

        Assume.That(layout.GetTextData().Single(g => g.PositionInString == 0).FontSize, Is.GreaterThan(FontSize));
        Assert.That(stops[0].X, Is.LessThan(stops[1].X));
        Assert.That(stops[1].X, Is.LessThan(stops[2].X));
    }

    // How far in from the right the lines beside a right-to-left cap start.
    private static double LayoutIndent(TextLayout layout)
    {
        var beside = Body(layout).Where(g => g.LineIndex == 1 && g.Symbol != ' ').Max(g => (double)g.Rect.Right);
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        return cap.PenX + cap.Advance - beside;
    }

    // A cap taking a carriage return and line feed in text with both directions is laid out, as in text with one.
    [Test]
    public void ADropCapTakingALineEnd_InTextOfBothDirections_IsLaidOut()
    {
        var layout = RightToLeft("א\r\n" + HebrewProse + " world", new DropCap(3, 2));

        Assert.That(layout.GetTextData().Single(g => g.PositionInString == 0).FontSize, Is.GreaterThan(FontSize));
    }

    // The text beside a right-to-left cap stands as far from its ink as beside a left-to-right one.
    [Test]
    public void TheGapBesideARightToLeftDropCap_IsTheMirrorOfALeftToRightOnes()
    {
        var typeface = Hebrew.Value;
        TextLayout Lay(TextDirection direction)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, DropCap = new DropCap(3), Direction = direction,
            };
            layout.ProcessText(HebrewProse, FontSize, new Size(Width, double.NaN), TextWrapping.WrapByWords,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout;
        }

        var rightToLeft = Lay(TextDirection.RightToLeft);
        var leftToRight = Lay(TextDirection.LeftToRight);
        var rtlCap = rightToLeft.GetTextData().Single(g => g.PositionInString == 0);
        var ltrCap = leftToRight.GetTextData().Single(g => g.PositionInString == 0);
        // A right-to-left line is set by its ink at its start, a left-to-right one by its pen.
        var rtlLine = Body(rightToLeft).Where(g => g.LineIndex == 1 && g.Symbol != ' ').Max(g => (double)g.Rect.Right);
        var ltrLine = Body(leftToRight).Where(g => g.LineIndex == 1).Min(g => g.PenX);

        Assert.That(rtlCap.Rect.Left - rtlLine, Is.EqualTo(ltrLine - ltrCap.Rect.Right).Within(0.5));
    }

    // The caret before the first character stands on the cap's right edge, after it on its left; a click finds it on
    // every line it spans.
    [TestCase(0)]
    [TestCase(2)]
    public void AClickOnARightToLeftDropCap_FindsIt_OnEveryLineItSpans(int line)
    {
        var layout = RightToLeft(HebrewProse, new DropCap(3));
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        var y = layout.GetLine(line).Top + layout.GetLine(line).Height / 2;
        var leading = layout.HitTest(cap.PenX + cap.Advance * 0.75, y);
        var trailing = layout.HitTest(cap.PenX + cap.Advance * 0.25, y);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetCaretPoint(new CaretPosition(0)).X, Is.EqualTo(cap.PenX + cap.Advance).Within(0.5));
            Assert.That((leading.Index, leading.CaretIndex, leading.IsInside), Is.EqualTo((0, 0, true)), "its right half");
            Assert.That((trailing.Index, trailing.CaretIndex, trailing.IsInside), Is.EqualTo((0, 1, true)), "its left half");
        });
    }

    [TestCase(1, 1)]
    [TestCase(3, 0)]
    public void ADropCapOfNoLinesOrCharacters_IsRejected(int lines, int characters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new DropCap(lines, characters));
    }
}
