using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class TextLayoutFallbackTests
{
    [SetUp]
    public void OnWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("the fallback families named here are Windows fonts");
        }
    }

    private static TextLayout Layout(AttributedText text, FontFallback fallback = null, bool setFallback = false)
    {
        var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf");
        var typeface = Typeface.LoadFont(path);
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        if (setFallback)
        {
            layout.Fallback = fallback;
        }

        layout.ProcessText(text, 20, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    [Test]
    public void ACharacterTheFontLacks_IsDrawnFromAFallbackFont()
    {
        var layout = Layout(new AttributedText("a中b"));
        var glyphs = layout.GetTextData();
        var han = glyphs.Single(g => g.PositionInString == 1);

        Assert.That(han.Font, Is.Not.SameAs(layout.Font));
        Assert.That(han.Glyph.Index, Is.Not.Zero, "a real glyph, not the missing-glyph box");
        Assert.That(glyphs.Where(g => g.PositionInString != 1).All(g => g.Font == layout.Font));
    }

    // The subset of Noto Color Emoji has 8 and 9 but no space, as Edge draws it.
    [Test]
    public void ASpaceTheFontLacks_IsTakenFromAFallbackFont()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "NotoColorEmoji.subset.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText("8 9", 20, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var glyphs = layout.GetTextData();
        var space = glyphs.Single(g => g.PositionInString == 1);

        Assert.That(space.Font, Is.Not.SameAs(layout.Font));
        Assert.That(space.Glyph.Index, Is.Not.Zero, "a real space, not the missing-glyph box");
        Assert.That(glyphs.Where(g => g.PositionInString != 1).All(g => g.Font == layout.Font));
    }

    // The font lacks the thin and the narrow no-break space; its space is set in their width, as HarfBuzz sets it.
    [TestCase(0x2009)]
    [TestCase(0x202F)]
    public void ASpaceTheFontSetsByItsOwnSpace_StaysInTheFont(int space)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "NotoSansHebrew-Regular.ttf"));
        Assume.That(typeface.Fonts[0].TryGetGlyphIndex(space, out _), Is.False);
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText("שלום" + (char)space + "עולם", 20, new Size(double.NaN, double.NaN), TextWrapping.NoWrap,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetTextData().All(g => g.Font == layout.Font), Is.True);
    }

    [Test]
    public void WithoutAFallback_TheMissingGlyphBoxIsDrawn()
    {
        var layout = Layout(new AttributedText("a中b"), setFallback: true);
        var han = layout.GetTextData().Single(g => g.PositionInString == 1);

        Assert.That(han.Font, Is.SameAs(layout.Font));
        Assert.That(han.Glyph.Index, Is.Zero);
    }

    [Test]
    public void HanFollowsTheLanguageOfTheText()
    {
        var japanese = Layout(new AttributedText("直", new TextAttributes { Language = "ja" }));
        var chinese = Layout(new AttributedText("直", new TextAttributes { Language = "zh" }));

        Assert.That(japanese.GetTextData().Single().Font.FontFamily, Does.Contain("Yu Gothic"));
        Assert.That(chinese.GetTextData().Single().Font.FontFamily, Does.Contain("YaHei"));
    }

    [Test]
    public void CjkPunctuationAndFullWidthForms_FollowTheLanguageOfTheText()
    {
        const string text = "「直。」、（Ａ）";
        var japanese = Layout(new AttributedText(text, new TextAttributes { Language = "ja" }));
        var chinese = Layout(new AttributedText(text, new TextAttributes { Language = "zh" }));

        Assert.That(japanese.GetTextData().Select(g => g.Font.FontFamily), Is.All.Contain("Yu Gothic"));
        Assert.That(chinese.GetTextData().Select(g => g.Font.FontFamily), Is.All.Contain("YaHei"));
    }

    [TestCase("\U00003299\U0000FE0F")]
    [TestCase("\U0000303D\U0000FE0F")]
    public void ACjkSymbolAskedForAsAnEmoji_IsDrawnFromTheEmojiFont(string text)
    {
        var layout = Layout(new AttributedText(text, new TextAttributes { Language = "ja" }));

        Assert.That(layout.GetTextData().First().Font.FontFamily, Is.EqualTo("Segoe UI Emoji"));
    }

    [Test]
    public void AnEmoji_IsDrawnFromTheEmojiFont()
    {
        var layout = Layout(new AttributedText("ok \U0001F600"));
        var emoji = layout.GetTextData().Single(g => g.PositionInString == 3);

        Assert.That(emoji.Font.FontFamily, Is.EqualTo("Segoe UI Emoji"));
        Assert.That(emoji.Glyph.Index, Is.Not.Zero);
        Assert.That(emoji.Glyph.BoundingRectangle.Width, Is.GreaterThan(0), "the uncolored glyph has an outline to draw");
    }

    [Test]
    public void VS16_TakesAPictographTheFontHasToTheEmojiFont()
    {
        var bare = Layout(new AttributedText("\U00002764"));
        var emoji = Layout(new AttributedText("\U00002764\U0000FE0F"));

        Assert.That(bare.GetTextData().Single().Font, Is.SameAs(bare.Font));
        Assert.That(emoji.GetTextData().Select(g => g.Font.FontFamily), Is.All.EqualTo("Segoe UI Emoji"));
    }

    [Test]
    public void VS16_AfterALetter_KeepsTheTextsFont()
    {
        var layout = Layout(new AttributedText("a\U0000FE0F"));

        Assert.That(layout.GetTextData().First().Font, Is.SameAs(layout.Font));
    }

    [Test]
    public void VS15_KeepsAnEmojiOutOfTheEmojiFont()
    {
        var layout = Layout(new AttributedText("\U00002615\U0000FE0E"));
        var coffee = layout.GetTextData().First();

        Assert.That(coffee.Font.FontFamily, Is.Not.EqualTo("Segoe UI Emoji"));
        Assert.That(coffee.Glyph.Index, Is.Not.Zero);
    }

    [TestCase("1\U0000FE0F\U000020E3", TestName = "A keycap")]
    [TestCase("\U0001F469\U0000200D\U00002764\U0000FE0F\U0000200D\U0001F468",
        TestName = "A sequence joined around a text-default pictograph")]
    [TestCase("\U0001F44D\U0001F3FD", TestName = "A skin tone")]
    [TestCase("\U0001F3F3\U0000FE0F\U0000200D\U0001F308", TestName = "A flag joined from a pictograph and a rainbow")]
    public void ASequence_IsDrawnWhollyFromTheEmojiFont(string sequence)
    {
        var glyphs = SequenceGlyphs(sequence);

        Assert.That(glyphs.Select(g => g.Font.FontFamily), Is.All.EqualTo("Segoe UI Emoji"));
        Assert.That(glyphs.Select(g => g.Glyph.Index), Is.All.Not.Zero);
    }

    [TestCase("1\U0000FE0F\U000020E3", TestName = "A keycap")]
    [TestCase("\U00002764\U0000FE0F", TestName = "A heart with VS16")]
    [TestCase("\U0001F469\U0001F3FE\U0000200D\U0001F4BB", TestName = "A joined sequence with a skin tone")]
    [TestCase("\U0001F3F3\U0000FE0F\U0000200D\U0001F308", TestName = "A flag joined from a pictograph and a rainbow")]
    public void ASequence_ShapesToOneGlyph(string sequence)
    {
        Assert.That(SequenceGlyphs(sequence), Has.Count.EqualTo(1));
    }

    [TestCase("\U0001F468\U0000200D\U0001F469\U0000200D\U0001F467", TestName = "A family")]
    [TestCase("\U0001F469\U0000200D\U00002764\U0000FE0F\U0000200D\U0001F48B\U0000200D\U0001F468", TestName = "A kiss")]
    public void AJoinerTheFontReplaced_IsNotHidden(string sequence)
    {
        Assert.That(SequenceGlyphs(sequence).Select(g => g.Glyph.BoundingRectangle.Width), Is.All.GreaterThan(0));
    }

    private static List<GlyphWordData> SequenceGlyphs(string sequence)
    {
        var layout = Layout(new AttributedText("a" + sequence + "b"));
        return layout.GetTextData().Where(g => g.PositionInString > 0 && g.PositionInString <= sequence.Length).ToList();
    }
}
