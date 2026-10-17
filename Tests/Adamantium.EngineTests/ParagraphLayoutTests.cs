using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Text broken a paragraph at a time: lines no worse than a line at a time by the composer's own measure (TeX's
/// demerits of each ragged line by its slack against a stretch of 2em), within the width, justified lines filling it,
/// and the last line of each paragraph left as it is.</summary>
[TestFixture]
public class ParagraphLayoutTests
{
    private const double FontSize = 20;

    private static readonly Lazy<Typeface> SourceSans = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf")));

    private const string Prose =
        "In olden times when wishing still helped one, there lived a king whose daughters were all beautiful, but " +
        "the youngest was so beautiful that the sun itself, which has seen so much, was astonished whenever it shone " +
        "in her face. Close by the king's castle lay a great dark forest, and under an old lime tree in the forest " +
        "was a well, and when the day was very warm, the king's child went out into the forest and sat down by the " +
        "side of the cool fountain.";

    private static TextLayout Layout(string text, double width, LineBreaking breaking,
        HorizontalTextAlignment alignment = HorizontalTextAlignment.Left, Hyphens hyphens = Hyphens.None,
        string language = null, double height = double.NaN, TextTrimming trimming = TextTrimming.None)
    {
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null,
            LineBreaking = breaking,
            Hyphens = hyphens,
            Language = language,
        };
        layout.ProcessText(text, FontSize, new Size(width, height), TextWrapping.WrapByWords, trimming, alignment,
            VerticalTextAlignment.Top);
        return layout;
    }

    private static GlyphWordData[][] Lines(TextLayout layout) =>
        layout.GetTextData().GroupBy(g => g.LineIndex).OrderBy(g => g.Key).Select(g => g.OrderBy(x => x.PenX).ToArray()).ToArray();

    private static double NaturalRight(GlyphWordData[] line)
    {
        var ink = line.Where(g => g.Symbol is not (' ' or '\t')).ToArray();
        return ink.Length == 0 ? 0 : ink.Max(g => g.PenX + g.Advance);
    }

    private static double RaggedDemerits(TextLayout layout, double width)
    {
        var lines = Lines(layout);
        double total = 0;
        var previousFitness = 2;
        for (var i = 0; i < lines.Length; i++)
        {
            var last = i == lines.Length - 1;
            var ratio = last ? 0 : (width - NaturalRight(lines[i])) / (2 * FontSize);
            var badness = Math.Min(100 * Math.Pow(Math.Max(ratio, 0), 3), 10000);
            var fitness = badness > 99 ? 0 : badness > 12 ? 1 : 2;
            var line = 10 + badness;
            total += line >= 10000 ? 1e8 : line * line;
            if (Math.Abs(fitness - previousFitness) > 1)
            {
                total += 10000;
            }

            previousFitness = fitness;
        }

        return total;
    }

    [TestCase(220)]
    [TestCase(260)]
    [TestCase(300)]
    [TestCase(360)]
    [TestCase(420)]
    public void ARaggedParagraph_IsNoWorseThanALineAtATime(double width)
    {
        var greedy = Layout(Prose, width, LineBreaking.Greedy);
        var paragraph = Layout(Prose, width, LineBreaking.Paragraph);

        Assert.That(RaggedDemerits(paragraph, width), Is.LessThanOrEqualTo(RaggedDemerits(greedy, width) + 1e-6));
        Assert.That(Lines(paragraph).Max(NaturalRight), Is.LessThanOrEqualTo(width + 1e-3), "every line fits");
    }

    [TestCase(220)]
    [TestCase(300)]
    [TestCase(420)]
    public void JustifiedLines_FillTheWidth_TheLastOneLeftAsItIs(double width)
    {
        var layout = Layout(Prose, width, LineBreaking.Paragraph, HorizontalTextAlignment.Justify);
        var lines = Lines(layout);

        for (var i = 0; i < lines.Length - 1; i++)
        {
            var ink = lines[i].Where(g => g.Symbol is not (' ' or '\t')).ToArray();
            Assert.That(ink.Max(g => g.Rect.Right), Is.EqualTo(width).Within(0.5), $"line {i}");
        }

        Assert.That(NaturalRight(lines[^1]), Is.LessThan(width - 1));
    }

    [Test]
    public void AJustifiedLine_MayShrinkItsSpaces()
    {
        var shrunk = Enumerable.Range(160, 200).Select(w => (Width: (double)w,
                Layout: Layout(Prose, w, LineBreaking.Paragraph, HorizontalTextAlignment.Justify)))
            .FirstOrDefault(c => Lines(c.Layout).SkipLast(1).Any(line => line.Any(g => g.Symbol == ' ')
                && line.Where(g => g.Symbol != ' ').Max(g => g.Rect.Right) <= c.Width + 0.5
                && Natural(line) > c.Width + 0.5));

        Assert.That(shrunk.Layout, Is.Not.Null, "some width has a line set tighter than its natural spacing");

        static double Natural(GlyphWordData[] line)
        {
            double pen = 0;
            foreach (var glyph in line)
            {
                pen += glyph.Advance;
            }

            return pen - line.Reverse().TakeWhile(g => g.Symbol == ' ').Sum(g => g.Advance);
        }
    }

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void TheLastLineOfAParagraph_IsNotJustified(LineBreaking breaking)
    {
        const string text = "one two three four five six seven eight nine ten\neleven twelve";
        var layout = Layout(text, 200, breaking, HorizontalTextAlignment.Justify);
        var newline = text.IndexOf('\n');
        var lines = Lines(layout);
        var lastOfFirst = lines.Single(line => line.Any(g => g.PositionInString == newline - 1));

        Assert.That(lastOfFirst.Where(g => g.Symbol != ' ').Max(g => g.Rect.Right), Is.LessThan(199));
    }

    [Test]
    public void Paragraph_HyphenatesWhereItHelps()
    {
        const string text =
            "Достопримечательности высокопроизводительного сельскохозяйственного производства неизменно привлекают " +
            "внимание любознательных путешественников.";
        var layout = Layout(text, 200, LineBreaking.Paragraph, HorizontalTextAlignment.Justify, Hyphens.Auto, "ru");
        var lines = Lines(layout);
        var hyphens = layout.GetTextData().Where(g => g.PositionInString < 0).ToArray();

        Assert.That(hyphens, Is.Not.Empty);
        foreach (var hyphen in hyphens)
        {
            Assert.That(lines[hyphen.LineIndex].Last(), Is.SameAs(hyphen), "a hyphen ends its line");
        }

        Assert.That(lines.Max(line => line.Where(g => g.Symbol != ' ').Max(g => g.Rect.Right)), Is.LessThanOrEqualTo(200.5));
    }

    [Test]
    public void Paragraph_KeepsEveryCharacterInOrder()
    {
        var layout = Layout(Prose, 240, LineBreaking.Paragraph, HorizontalTextAlignment.Justify, Hyphens.Auto, "en-US");
        var clusters = layout.GetTextData().Where(g => g.PositionInString >= 0).Select(g => g.PositionInString).ToArray();

        Assert.That(clusters, Is.Ordered);
        Assert.That(clusters.Distinct().Count(), Is.EqualTo(Prose.Length));
    }

    // Wide enough for every word whole: one wider than its line breaks whatever the limit.
    private const double Column = 240;

    private const string LongWords =
        "Достопримечательности высокопроизводительного сельскохозяйственного производства неизменно привлекают " +
        "внимание любознательных путешественников, интересующихся достижениями отечественного машиностроения.";

    [TestCase(LineBreaking.Greedy, 1)]
    [TestCase(LineBreaking.Greedy, 2)]
    [TestCase(LineBreaking.Paragraph, 1)]
    public void AHyphenLimit_EndsNoMoreLinesInARowInAHyphen(LineBreaking breaking, int limit)
    {
        int Run(TextLayout layout)
        {
            int longest = 0, run = 0;
            foreach (var line in Lines(layout))
            {
                run = line.Last(g => g.Symbol != ' ').Symbol == '-' ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }

            return longest;
        }

        var free = Layout(LongWords, Column, breaking, hyphens: Hyphens.Auto, language: "ru");
        var typeface = SourceSans.Value;
        var limited = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, LineBreaking = breaking, Hyphens = Hyphens.Auto, Language = "ru", HyphenLimit = limit,
        };
        limited.ProcessText(LongWords, FontSize, new Size(Column, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assume.That(Run(free), Is.GreaterThan(limit), "without a limit more lines in a row end in a hyphen");
        Assert.That(Run(limited), Is.LessThanOrEqualTo(limit));
        Assert.That(limited.GetTextData().Where(g => g.Symbol != ' ').Max(g => g.Rect.Right), Is.LessThanOrEqualTo(Column + 0.5));
    }

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void AWordWiderThanItsColumn_BreaksWhereItMust_WhateverTheLimit(LineBreaking breaking)
    {
        var typeface = SourceSans.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0])
        {
            Fallback = null, LineBreaking = breaking, Hyphens = Hyphens.Auto, Language = "ru", HyphenLimit = 1,
        };
        layout.ProcessText("высокопроизводительного", FontSize, new Size(60, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.LineCount, Is.GreaterThan(2));
        Assert.That(layout.GetTextData().Max(g => g.Rect.Right), Is.LessThanOrEqualTo(60.5));
    }

    [TestCase(LineBreaking.Greedy, HorizontalTextAlignment.Left)]
    [TestCase(LineBreaking.Paragraph, HorizontalTextAlignment.Left)]
    [TestCase(LineBreaking.Paragraph, HorizontalTextAlignment.Justify)]
    public void AHyphenLimit_LeavesTheHyphensOfTheTextUncounted(LineBreaking breaking, HorizontalTextAlignment alignment)
    {
        const string text = "state-of-the-art up-to-date mother-in-law long-term well-known self-made far-reaching";
        string Lines(int limit)
        {
            var typeface = SourceSans.Value;
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, LineBreaking = breaking, Hyphens = Hyphens.Manual, HyphenLimit = limit,
            };
            layout.ProcessText(text, FontSize, new Size(110, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                alignment, VerticalTextAlignment.Top);
            return string.Join("|", Enumerable.Range(0, layout.LineCount).Select(i => layout.GetLine(i).Start));
        }

        Assert.That(Lines(1), Is.EqualTo(Lines(0)));
    }

    [Test]
    public void AHyphenLimit_BelowZero_IsRejected()
    {
        var layout = new TextLayout(SourceSans.Value, SourceSans.Value.Fonts[0]);

        Assert.Throws<ArgumentOutOfRangeException>(() => layout.HyphenLimit = -1);
    }

    [Test]
    public void Paragraph_EndsTheLastLineTheHeightAllowsInAnEllipsis()
    {
        var lineHeight = Layout(Prose, 260, LineBreaking.Greedy).GetLine(0).Height;
        var layout = Layout(Prose, 260, LineBreaking.Paragraph, trimming: TextTrimming.WordEllipses, height: lineHeight * 3);
        var data = layout.GetTextData();

        Assert.That(data.Max(g => g.LineIndex), Is.EqualTo(2));
        Assert.That(data.Where(g => g.PositionInString < 0).Select(g => g.LineIndex), Is.EqualTo(new[] { 2, 2, 2 }));
        Assert.That(data.Max(g => g.Rect.Right), Is.LessThanOrEqualTo(260));
    }

    public static IEnumerable<TestCaseData> Samples()
    {
        (string Language, string Text)[] samples =
        [
            ("en-US", Prose),
            ("ru", "Достопримечательности высокопроизводительного сельскохозяйственного производства неизменно привлекают внимание любознательных путешественников."),
            ("de", "Die Donaudampfschifffahrtsgesellschaft veröffentlichte ihre Rechtschreibempfehlungen für Lebensversicherungsunternehmen."),
            ("fr", "L'internationalisation des applications typographiques extraordinairement sophistiquées nécessite des dictionnaires."),
            ("en-US", "Supercali­fragilistic­expiali­docious, even though the sound of it is something quite atro­cious."),
            ("en-US", "Short\nSupercalifragilisticexpialidocious\n\nwords  with   spaces\tand a tab"),
        ];
        foreach (var (language, text) in samples)
        {
            foreach (var hyphens in new[] { Hyphens.None, Hyphens.Manual, Hyphens.Auto })
            {
                foreach (var alignment in new[] { HorizontalTextAlignment.Left, HorizontalTextAlignment.Justify })
                {
                    yield return new TestCaseData(language, text, hyphens, alignment)
                        .SetName($"{language} {text.Substring(0, 5)} {hyphens} {alignment}");
                }
            }
        }
    }

    [TestCaseSource(nameof(Samples))]
    public void EveryWidth_KeepsLinesWithSpacesWithinIt(string language, string text, Hyphens hyphens,
        HorizontalTextAlignment alignment)
    {
        for (var width = 80; width <= 480; width += 10)
        {
            var layout = Layout(text, width, LineBreaking.Paragraph, alignment, hyphens, language);
            foreach (var line in Lines(layout))
            {
                var ink = line.Where(g => g.Symbol is not (' ' or '\t' or '\n')).ToArray();
                if (ink.Length == 0)
                {
                    continue;
                }

                var spaced = line.Any(g => g.Symbol is ' ' or '\t' && g.PenX > ink.Min(i => i.PenX) && g.PenX < ink.Max(i => i.PenX));
                if (spaced)
                {
                    Assert.That(ink.Max(g => g.Rect.Right), Is.LessThanOrEqualTo(width + 0.5),
                        $"width {width}, line {line[0].LineIndex}");
                }
            }
        }
    }

    [TestCase(HorizontalTextAlignment.Left)]
    [TestCase(HorizontalTextAlignment.Justify)]
    public void AWordTheComposerCutsAtALinesEnd_StartsOnThatLine(HorizontalTextAlignment alignment)
    {
        const string text = "Высокопроизводительные текстовые редакторы переносят слова автоматически.";
        for (var width = 220; width <= 320; width += 5)
        {
            var lines = Lines(Layout(text, width, LineBreaking.Paragraph, alignment, Hyphens.Auto, "ru"));
            for (var i = 0; i < lines.Length - 1; i++)
            {
                var ink = lines[i].Where(g => g.Symbol is not (' ' or '\t' or '\n')).ToArray();
                Assert.That(ink.Sum(g => g.Advance), Is.GreaterThan(width - 4 * FontSize),
                    $"width {width}: line {i} is left nearly empty");
            }
        }
    }

    [Test]
    public void Paragraph_SetsAWordWiderThanTheLineAlone()
    {
        const string text = "a Supercalifragilisticexpialidocious b";
        var layout = Layout(text, 120, LineBreaking.Paragraph);
        var lines = Lines(layout);

        Assert.That(lines.Length, Is.EqualTo(3));
        Assert.That(lines[1].Where(g => g.Symbol != ' ').Select(g => g.PositionInString).First(), Is.EqualTo(2));
    }
}
