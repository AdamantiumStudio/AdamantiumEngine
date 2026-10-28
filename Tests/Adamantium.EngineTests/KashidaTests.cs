using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Justified Arabic stretched with kashidas: once its spaces are at their maximum, a line widens one joint of
/// each word with a stretched extender - the font's 'JSTF' glyph, else its tatweel - and fills its width.</summary>
[TestFixture]
public class KashidaTests
{
    private const double FontSize = 20;
    private const double Width = 300;

    // The Universal Declaration of Human Rights, article 1.
    private const string Arabic =
        "يولد جميع الناس أحرارًا متساوين في الكرامة والحقوق. وقد وهبوا عقلاً وضميرًا وعليهم أن يعامل بعضهم بعضًا " +
        "بروح الإخاء.";

    private static TextLayout Lay(string fontPath, bool kashidas, LineBreaking breaking = LineBreaking.Greedy)
    {
        var typeface = Typeface.LoadFont(fontPath);
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, Kashidas = kashidas, LineBreaking = breaking, WordSpacing = new SpacingRange(1, 1, 1.2),
        };
        layout.ProcessText(Arabic, FontSize, new Size<double>(Width, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);
        return layout;
    }

    private static string NotoSansArabic => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansArabic-Regular.ttf");

    private static GlyphWordData[] Extenders(TextLayout layout) =>
        layout.GetTextData().Where(glyph => glyph.PositionInString < 0 && glyph.Symbol == 'ـ').ToArray();

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void JustifiedArabic_FillsItsLinesWithKashidas(LineBreaking breaking)
    {
        var layout = Lay(NotoSansArabic, true, breaking);
        var glyphs = layout.GetTextData();
        var extenders = Extenders(layout);

        Assert.That(extenders, Is.Not.Empty);
        for (var line = 0; line < layout.LineCount - 1; line++)
        {
            var ink = glyphs.Where(glyph => glyph.LineIndex == line && glyph.Symbol != ' ').ToArray();
            Assert.That(ink.Min(glyph => glyph.Rect.Left), Is.EqualTo(0).Within(2), $"line {line}");
            Assert.That(ink.Max(glyph => glyph.Rect.Right), Is.EqualTo(Width).Within(2), $"line {line}");
            Assert.That(extenders.Count(extender => extender.LineIndex == line),
                Is.LessThanOrEqualTo(glyphs.Count(glyph => glyph.LineIndex == line && glyph.Symbol == ' ') + 1),
                $"line {line}: at most one kashida a word");
        }

        Assert.That(extenders.Any(extender => extender.LineIndex == layout.LineCount - 1), Is.False,
            "the last line is not stretched");
    }

    [Test]
    public void AKashida_JoinsTheTwoLettersItStandsBetween()
    {
        var layout = Lay(NotoSansArabic, true);
        var glyphs = layout.GetTextData();

        foreach (var extender in Extenders(layout))
        {
            var line = glyphs.Where(glyph => glyph.LineIndex == extender.LineIndex && glyph.PositionInString >= 0
                                             && glyph.Symbol != ' ' && glyph.Rect.Width > 0).ToArray();
            Assert.That(line.Any(glyph => glyph.Rect.Left < extender.Rect.Left && glyph.Rect.Right > extender.Rect.Left - 1),
                Is.True, "a letter meets it on its left");
            Assert.That(line.Any(glyph => glyph.Rect.Right > extender.Rect.Right && glyph.Rect.Left < extender.Rect.Right + 1),
                Is.True, "a letter meets it on its right");
        }
    }

    [Test]
    public void AKashida_StandsBetweenTwoLogicallyAdjacentJoinedLetters()
    {
        AssertBetweenJoinedLetters(Arabic, Lay(NotoSansArabic, true).GetTextData());
    }

    [Test]
    public void InALeftToRightParagraph_NoKashidaJoinsALatinLetterToAnArabicOne()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("the fallback for the Latin letters is a Windows font");
        }

        const string text = "Product abcمحمد and 2024علي with سلسلة words, then more words to justify the line again.";
        var typeface = Typeface.LoadFont(NotoSansArabic);
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Direction = TextDirection.LeftToRight };
        layout.ProcessText(text, FontSize, new Size<double>(220, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);

        Assert.That(Extenders(layout), Is.Not.Empty, "some line took a kashida");
        AssertBetweenJoinedLetters(text, layout.GetTextData());
    }

    private static void AssertBetweenJoinedLetters(string text, GlyphWordData[] glyphs)
    {
        for (var i = 0; i < glyphs.Length; i++)
        {
            if (glyphs[i].PositionInString >= 0 || glyphs[i].Symbol != 'ـ')
            {
                continue;
            }

            var later = glyphs[i - 1].PositionInString;
            var letter = later - 1;
            while (letter > 0 && char.GetUnicodeCategory(text[letter]) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                letter--;
            }

            Assert.That(text[later], Is.InRange('ؠ', 'ۿ'), $"the letter after the kashida, at {later}");
            Assert.That(text[letter], Is.InRange('ؠ', 'ۿ'), $"the letter before the kashida, at {letter}");
            Assert.That(Adamantium.Fonts.Text.CursiveScripts.JoinsNext(text, letter), Is.True, $"{text[letter]} joins {text[later]}");
        }
    }

    [Test]
    public void WithoutKashidas_SpacesTakeTheRoom()
    {
        var layout = Lay(NotoSansArabic, false);

        Assert.That(Extenders(layout), Is.Empty);
        var glyphs = layout.GetTextData();
        Assert.That(glyphs.Where(glyph => glyph.LineIndex == 0 && glyph.Symbol != ' ').Max(glyph => glyph.Rect.Right),
            Is.EqualTo(Width).Within(2));
    }

    [Test]
    public void TheJustificationTable_GivesTheExtender()
    {
        var arial = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (!File.Exists(arial))
        {
            Assert.Ignore("Arial is a Windows font");
        }

        var font = Typeface.LoadFont(arial).Fonts[0];
        var extenders = font.GetJustificationExtenders("arab");

        Assert.That(extenders, Is.Not.Empty);
        var used = Extenders(Lay(arial, true));
        Assert.That(used, Is.Not.Empty);
        Assert.That(used.Select(glyph => glyph.Glyph.Index), Is.All.EqualTo(extenders[0]));
    }
}
