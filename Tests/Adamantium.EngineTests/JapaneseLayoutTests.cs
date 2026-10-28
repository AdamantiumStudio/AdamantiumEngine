using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Japanese set as JLREQ and CSS set it: justified lines reach the edge widened between their letters,
/// fullwidth punctuation side by side collapses its blanks (text-spacing-trim), and a comma or full stop at the end of a
/// line hangs past it (burasage).</summary>
[TestFixture]
public class JapaneseLayoutTests
{
    private const double FontSize = 20;

    private static readonly Lazy<Typeface> NotoSansCjk = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansCJK-Regular.ttc")));

    private static TextLayout Lay(string text, double width, HorizontalTextAlignment alignment = HorizontalTextAlignment.Left,
        TextSpacingTrim trim = TextSpacingTrim.Normal, HangingPunctuation hanging = HangingPunctuation.AllowEnd,
        LineBreaking breaking = LineBreaking.Greedy)
    {
        var typeface = NotoSansCjk.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, SpacingTrim = trim, HangingPunctuation = hanging, LineBreaking = breaking, Language = "ja",
        };
        layout.ProcessText(text, FontSize, new Size<double>(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            alignment, VerticalTextAlignment.Top);
        return layout;
    }

    private static GlyphWordData At(TextLayout layout, int index) =>
        layout.GetTextData().First(glyph => glyph.PositionInString == index);

    // With no spaces and letter spacing at its default of none, a justified Japanese line stopped short of the edge.
    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void AJustifiedLine_ReachesTheEdge_WidenedBetweenItsLetters(LineBreaking breaking)
    {
        const string text = "吾輩は猫である名前はまだ無いどこで生れたかとんと見当がつかぬ何でも薄暗いじめじめした所で泣いていた";
        var layout = Lay(text, 210, HorizontalTextAlignment.Justify, breaking: breaking);
        var glyphs = layout.GetTextData();

        Assert.That(layout.LineCount, Is.GreaterThan(2));
        for (var line = 0; line < layout.LineCount - 1; line++)
        {
            var ink = glyphs.Where(glyph => glyph.LineIndex == line).ToArray();
            Assert.That(ink.Min(glyph => glyph.Rect.Left), Is.LessThan(FontSize / 2), $"line {line} starts at the start");
            Assert.That(ink.Max(glyph => glyph.Rect.Right), Is.EqualTo(210).Within(FontSize / 2), $"line {line} reaches the end");
        }
    }

    // A closing bracket before an opening one: the opening one is set half-width, so the pair takes an em and a half.
    [TestCase(TextSpacingTrim.Normal, 0.5)]
    [TestCase(TextSpacingTrim.SpaceAll, 1.0)]
    public void AnOpeningBracketAfterAClosingOne_IsSetHalfWidth(TextSpacingTrim trim, double ems)
    {
        var layout = Lay("語」「語", 400, trim: trim);

        Assert.Multiple(() =>
        {
            Assert.That(At(layout, 2).PenX - At(layout, 1).PenX, Is.EqualTo(FontSize).Within(0.5), "」 full");
            Assert.That(At(layout, 3).PenX - At(layout, 2).PenX, Is.EqualTo(ems * FontSize).Within(0.5), "「 as set");
        });
    }

    // Two closing brackets side by side: the first gives up its blank half.
    [Test]
    public void AClosingBracketBeforeAnother_IsSetHalfWidth()
    {
        var layout = Lay("語）」語", 400);

        Assert.That(At(layout, 2).PenX - At(layout, 1).PenX, Is.EqualTo(FontSize / 2).Within(0.5));
    }

    // An opening bracket starting a line: full-width for Normal, half-width for TrimStart.
    [TestCase(TextSpacingTrim.Normal, 1.0)]
    [TestCase(TextSpacingTrim.TrimStart, 0.5)]
    public void AnOpeningBracketStartingALine_IsSetAsTheTrimSays(TextSpacingTrim trim, double ems)
    {
        // Five letters a line: the bracket starts the second.
        var layout = Lay("吾輩は猫で「名前」", 5 * FontSize + 1, trim: trim);
        var bracket = At(layout, 5);

        Assert.Multiple(() =>
        {
            Assert.That(bracket.LineIndex, Is.EqualTo(1));
            Assert.That(At(layout, 6).PenX - bracket.PenX, Is.EqualTo(ems * FontSize).Within(0.5));
        });
    }

    // A full stop that does not fit hangs past the end of the line rather than taking the letter before it along.
    [TestCase(HangingPunctuation.AllowEnd, 0)]
    [TestCase(HangingPunctuation.None, 1)]
    public void AFullStopAtTheEnd_HangsPastIt(HangingPunctuation hanging, int stopLine)
    {
        var layout = Lay("吾輩は猫で。名前", 5 * FontSize + 1, hanging: hanging);

        Assert.That(At(layout, 5).LineIndex, Is.EqualTo(stopLine));
    }

    // Justified with a full stop hanging, the line's last letter reaches the edge and the stop stands past it.
    [Test]
    public void AJustifiedLine_LeavesItsHangingStopOut()
    {
        var layout = Lay("吾輩は猫で。名前はまだ無い", 5 * FontSize + 1, HorizontalTextAlignment.Justify);

        Assert.Multiple(() =>
        {
            Assert.That(At(layout, 4).Rect.Right, Is.EqualTo(5 * FontSize + 1).Within(FontSize / 2), "で at the edge");
            Assert.That(At(layout, 5).PenX, Is.GreaterThanOrEqualTo(5 * FontSize), "。 past it");
        });
    }

    // A word broken at a hyphen is set again in its halves: the punctuation beside it trimmed as before.
    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void AWordSetAgainInHalves_KeepsItsPunctuationTrimmed(LineBreaking breaking)
    {
        const string text = "（internationalization）」";
        var whole = Lay(text, 2000, breaking: breaking);
        var width = whole.RealTextDimensions.Width * 0.6;
        var typeface = NotoSansCjk.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, LineBreaking = breaking, Language = "en-US", Hyphens = Hyphens.Auto,
        };
        layout.ProcessText(text, FontSize, new Size<double>(width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var close = text.IndexOf('）');

        Assume.That(layout.LineCount, Is.GreaterThan(1), "hyphenated");
        Assert.That(At(layout, close + 1).PenX - At(layout, close).PenX, Is.EqualTo(FontSize / 2).Within(0.5));
    }

    // A closing bracket that would not fit at the end of a line gives up its blank half and stays.
    [Test]
    public void AClosingBracketThatWouldNotFit_IsTrimmedToStay()
    {
        var layout = Lay("吾輩は猫」名前", 4.6 * FontSize, hanging: HangingPunctuation.None);
        var bracket = At(layout, 4);

        Assert.Multiple(() =>
        {
            Assert.That(bracket.LineIndex, Is.EqualTo(0), "」 stays on the line");
            Assert.That(bracket.Advance, Is.EqualTo(FontSize / 2).Within(0.5), "half-width");
        });
    }
}
