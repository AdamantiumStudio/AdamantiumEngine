using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Ruby, as HTML's: an annotation centered over its base, a base spread to a longer annotation, a line grown
/// to hold it unless its height is set, the annotation under the line when asked, a base kept on one line.</summary>
[TestFixture]
public class RubyTests
{
    private const double FontSize = 20;
    private const string Text = "日本の漢字を読む";
    private const int Base = 3;

    private static readonly Lazy<Typeface> NotoSansCjk = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansCJK-Regular.ttc")));

    private static TextLayout Lay(string reading, RubyPosition position = RubyPosition.Over, double width = 400,
        Action<TextLayout> setUp = null, string text = Text, int start = Base, int length = 2)
    {
        var typeface = NotoSansCjk.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        setUp?.Invoke(layout);
        var attributed = new AttributedText(text);
        if (reading != null)
        {
            attributed.SetRuby(start, length, new RubyAnnotation(reading, position));
        }

        layout.ProcessText(attributed, FontSize, new Size(width, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static GlyphWordData[] Annotation(TextLayout layout) =>
        layout.GetTextData().Where(glyph => glyph.PositionInString < 0).ToArray();

    private static GlyphWordData[] BaseGlyphs(TextLayout layout, int start = Base, int length = 2) =>
        layout.GetTextData().Where(glyph => glyph.PositionInString >= start && glyph.PositionInString < start + length)
            .ToArray();

    private static double Left(GlyphWordData[] glyphs) => glyphs.Min(glyph => glyph.PenX);

    private static double Right(GlyphWordData[] glyphs) => glyphs.Max(glyph => glyph.PenX + glyph.Advance);

    [Test]
    public void TheAnnotation_StandsCenteredOverItsBase_AtHalfItsSize()
    {
        var layout = Lay("かんじ");
        var annotation = Annotation(layout);
        var bases = BaseGlyphs(layout);

        Assert.Multiple(() =>
        {
            Assert.That(annotation.Select(glyph => glyph.Symbol), Is.EqualTo("かんじ"));
            Assert.That(annotation.All(glyph => glyph.FontSize.Equals(FontSize / 2)), Is.True);
            Assert.That((Left(annotation) + Right(annotation)) / 2, Is.EqualTo((Left(bases) + Right(bases)) / 2).Within(0.01));
            Assert.That(annotation.Max(glyph => glyph.Rect.Bottom), Is.LessThanOrEqualTo(bases.Min(glyph => glyph.Rect.Top) + 0.5));
            Assert.That(annotation.Min(glyph => glyph.Rect.Top), Is.GreaterThanOrEqualTo(layout.GetLine(0).Top - 0.5),
                "inside its line");
        });
    }

    [Test]
    public void ALongerAnnotation_SpreadsItsBase_SpaceAroundEachGlyph()
    {
        const string reading = "とてもながいよみがな";
        var plain = Lay(null);
        var layout = Lay(reading);
        var annotation = Annotation(layout);
        var bases = BaseGlyphs(layout);
        var plainBases = BaseGlyphs(plain);

        Assert.Multiple(() =>
        {
            Assert.That(Right(bases) - Left(bases), Is.EqualTo(Right(annotation) - Left(annotation)).Within(0.01));
            Assert.That(bases[0].Advance, Is.EqualTo(bases[1].Advance).Within(1e-6), "the room shared alike");
            Assert.That(bases[0].Rect.Left - Left(bases),
                Is.EqualTo(plainBases[0].Rect.Left - Left(plainBases) + (bases[0].Advance - plainBases[0].Advance) / 2)
                    .Within(0.01), "half of its share before each glyph");
            Assert.That(layout.GetTextData().First(glyph => glyph.PositionInString == Base + 2).PenX,
                Is.EqualTo(Right(bases)).Within(1e-6), "the text after it follows the spread base");
        });
    }

    [Test]
    public void TheLine_GrowsToHoldTheAnnotation()
    {
        var plain = Lay(null).GetLine(0);
        var line = Lay("かんじ").GetLine(0);

        Assert.Multiple(() =>
        {
            Assert.That(line.Height, Is.GreaterThan(plain.Height + FontSize / 4));
            Assert.That(line.Baseline - line.Top, Is.GreaterThan(plain.Baseline - plain.Top + FontSize / 4));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ALineWhoseHeightIsSet_DoesNotGrow(bool blockLineHeight)
    {
        void Set(TextLayout layout)
        {
            if (blockLineHeight)
            {
                layout.LineStacking = LineStackingStrategy.BlockLineHeight;
            }
            else
            {
                layout.LineHeight = 30;
            }
        }

        var plain = Lay(null, setUp: Set).GetLine(0);
        var line = Lay("かんじ", setUp: Set).GetLine(0);

        Assert.Multiple(() =>
        {
            Assert.That(line.Height, Is.EqualTo(plain.Height).Within(0.01));
            Assert.That(line.Baseline, Is.EqualTo(plain.Baseline).Within(0.01));
        });
    }

    [Test]
    public void UnderTheLine_TheAnnotationStandsBelowItsBase_AndTheLineGrowsDown()
    {
        var plain = Lay(null).GetLine(0);
        var layout = Lay("かんじ", RubyPosition.Under);
        var line = layout.GetLine(0);
        var annotation = Annotation(layout);
        var bases = BaseGlyphs(layout);

        Assert.Multiple(() =>
        {
            Assert.That(annotation.Min(glyph => glyph.Rect.Top), Is.GreaterThanOrEqualTo(bases.Max(glyph => glyph.Rect.Bottom) - 0.5));
            Assert.That(line.Baseline - line.Top, Is.EqualTo(plain.Baseline - plain.Top).Within(0.01), "nothing added above");
            Assert.That(line.Height, Is.GreaterThan(plain.Height + FontSize / 4));
        });
    }

    [TestCase(RubyPosition.Over)]
    [TestCase(RubyPosition.Under)]
    public void InVerticalText_TheAnnotationStandsUprightBesideItsBase(RubyPosition position)
    {
        TextLayout Vertical(string reading)
        {
            var typeface = NotoSansCjk.Value;
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, WritingMode = WritingMode.VerticalRightToLeft,
            };
            var attributed = new AttributedText(Text);
            if (reading != null)
            {
                attributed.SetRuby(Base, 2, new RubyAnnotation(reading, position));
            }

            layout.ProcessText(attributed, FontSize, new Size(200, 400), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout;
        }

        var layout = Vertical("かんじ");
        var annotation = Annotation(layout);
        var bases = BaseGlyphs(layout);
        var all = layout.GetTextData().Where(glyph => glyph.PositionInString >= 0).ToArray();
        double Middle(GlyphWordData[] glyphs, Func<RectangleF, double> low, Func<RectangleF, double> high) =>
            (glyphs.Min(glyph => low(glyph.Rect)) + glyphs.Max(glyph => high(glyph.Rect))) / 2;

        Assert.Multiple(() =>
        {
            Assert.That(annotation.All(glyph => glyph.Upright), Is.True);
            Assert.That(Middle(annotation, rect => rect.Top, rect => rect.Bottom),
                Is.EqualTo(Middle(bases, rect => rect.Top, rect => rect.Bottom)).Within(FontSize / 4), "centered along it");
            if (position == RubyPosition.Over)
            {
                Assert.That(annotation.Min(glyph => glyph.Rect.Left), Is.GreaterThanOrEqualTo(bases.Max(glyph => glyph.Rect.Right) - 0.5),
                    "to its right");
            }
            else
            {
                Assert.That(annotation.Max(glyph => glyph.Rect.Right), Is.LessThanOrEqualTo(bases.Min(glyph => glyph.Rect.Left) + 0.5),
                    "to its left");
            }

            Assert.That(annotation.Max(glyph => glyph.Rect.Right), Is.LessThanOrEqualTo(200.5), "inside the area");
            var centers = all.Select(glyph => glyph.Rect.Left + glyph.Rect.Width / 2).ToArray();
            Assert.That(centers.Max() - centers.Min(), Is.LessThan(FontSize / 4), "the line's own text stays in one column");
        });
    }

    [Test]
    public void ABase_StaysOnOneLine()
    {
        const string text = "漢字漢字漢字漢字漢字";
        var layout = Lay("かんじかんじ", width: 3.5 * FontSize, text: text, start: 2, length: 4);
        var bases = BaseGlyphs(layout, 2, 4);

        Assert.That(bases.Select(glyph => glyph.LineIndex).Distinct().Count(), Is.EqualTo(1),
            string.Join(", ", layout.GetTextData().Select(glyph => $"{glyph.Symbol}{glyph.LineIndex}")));
    }

    private static readonly Lazy<Typeface> SourceSans = new(() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf")));

    private static TextLayout LayIn(Lazy<Typeface> face, AttributedText attributed, double width,
        TextWrapping wrapping = TextWrapping.WrapByWords, Action<TextLayout> setUp = null, double height = double.NaN,
        TextTrimming trimming = TextTrimming.None)
    {
        var layout = new TextLayout(face.Value, face.Value.Fonts[0]) { Fallback = null };
        setUp?.Invoke(layout);
        layout.ProcessText(attributed, FontSize, new Size(width, height), wrapping, trimming,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static int[] LinesOf(TextLayout layout, int start, int end) => layout.GetTextData()
        .Where(glyph => glyph.PositionInString >= start && glyph.PositionInString < end)
        .Select(glyph => glyph.LineIndex).Distinct().ToArray();

    [TestCase(LineBreaking.Greedy)]
    [TestCase(LineBreaking.Paragraph)]
    public void ABaseWithASpace_StaysOnOneLine(LineBreaking breaking)
    {
        var text = new AttributedText("aaaa bbbb cccc dddd").SetRuby(5, 9, new RubyAnnotation("ruby"));
        var layout = LayIn(SourceSans, text, 90, setUp: set => set.LineBreaking = breaking);

        Assert.That(LinesOf(layout, 5, 14), Has.Length.EqualTo(1));
    }

    [TestCase(Hyphens.Manual)]
    [TestCase(Hyphens.Auto)]
    public void ABase_IsNotHyphenated(Hyphens hyphens)
    {
        var text = new AttributedText("xx inter­national yy").SetRuby(3, 14, new RubyAnnotation("word"));
        var layout = LayIn(SourceSans, text, 60, setUp: set => set.Hyphens = hyphens);

        Assert.That(LinesOf(layout, 3, 17), Has.Length.EqualTo(1));
    }

    [Test]
    public void ABase_IsNotBrokenBySymbols()
    {
        var text = new AttributedText("漢字漢字漢字漢字漢字").SetRuby(2, 4, new RubyAnnotation("かんじかんじ"));
        var layout = LayIn(NotoSansCjk, text, 70, TextWrapping.WrapBySymbols);

        Assert.That(LinesOf(layout, 2, 6), Has.Length.EqualTo(1));
    }

    [Test]
    public void ABase_EndsAtAHardBreak()
    {
        var layout = LayIn(NotoSansCjk, new AttributedText("日本の漢\n字を読む").SetRuby(3, 3, new RubyAnnotation("かん")), 400);
        var annotation = Annotation(layout);
        var bases = BaseGlyphs(layout, 3, 1);

        Assert.That((Left(annotation) + Right(annotation)) / 2, Is.EqualTo((Left(bases) + Right(bases)) / 2).Within(0.01));
    }

    [Test]
    public void TheAnnotation_ClearsTheTallestGlyphOfItsBase()
    {
        var text = new AttributedText(Text).Apply(4, 1, new TextAttributes { FontSize = 40 })
            .SetRuby(Base, 2, new RubyAnnotation("かんじ"));
        var layout = LayIn(NotoSansCjk, text, 400);

        Assert.That(Annotation(layout).Max(glyph => glyph.Rect.Bottom),
            Is.LessThanOrEqualTo(BaseGlyphs(layout).Min(glyph => glyph.Rect.Top) + 0.5));
    }

    [Test]
    public void TheAnnotation_ClearsAnObjectBase()
    {
        var text = new AttributedText("ab￼cd").Apply(2, 1, new TextAttributes { ObjectSize = new Size(30, 30) })
            .SetRuby(2, 1, new RubyAnnotation("box"));
        var layout = LayIn(SourceSans, text, 400);
        var box = layout.GetTextData().Single(glyph => glyph.PositionInString == 2);

        Assert.That(Annotation(layout).Max(glyph => glyph.Rect.Bottom), Is.LessThanOrEqualTo(box.Rect.Top + 0.5));
    }

    [Test]
    public void ASpreadBase_KeepsItsMarksOnItsLetters()
    {
        var hebrew = new Lazy<Typeface>(() =>
            Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansHebrew-Regular.ttf")));
        const string word = "שָׁלוֹם";
        // Each mark's ink against the ink of the letter it sits on: the nearest letter of its cluster or before it.
        double[] MarkOffsets(TextLayout layout)
        {
            var glyphs = layout.GetTextData().Where(glyph => glyph.PositionInString >= 0).ToArray();
            return glyphs.Where(glyph => glyph.Advance == 0)
                .Select(mark => (double)mark.Rect.X - glyphs.Where(letter => letter.Advance > 0
                                                                     && letter.PositionInString <= mark.PositionInString)
                    .MaxBy(letter => letter.PositionInString).Rect.X)
                .ToArray();
        }

        var plain = MarkOffsets(LayIn(hebrew, new AttributedText(word), 400));
        var spread = MarkOffsets(LayIn(hebrew, new AttributedText(word)
            .SetRuby(0, word.Length, new RubyAnnotation("a very long annotation here")), 400));

        Assert.That(spread, Is.EqualTo(plain).Within(0.01));
    }

    [Test]
    public void ASpreadJoinedBase_StaysJoined()
    {
        var arabic = new Lazy<Typeface>(() =>
            Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "NotoSansArabic-Regular.ttf")));
        const string word = "سلام";
        // Where each letter's ink stands against the first's: joined letters keep their places to each other.
        double[] Gaps(TextLayout layout)
        {
            var glyphs = layout.GetTextData().Where(glyph => glyph.PositionInString >= 0)
                .OrderBy(glyph => glyph.PositionInString).ToArray();
            return glyphs.Select(glyph => (double)glyph.Rect.X - glyphs[0].Rect.X).ToArray();
        }

        var plain = Gaps(LayIn(arabic, new AttributedText(word), 400));
        var spread = Gaps(LayIn(arabic, new AttributedText(word)
            .SetRuby(0, word.Length, new RubyAnnotation("a very long annotation here")), 400));

        Assert.That(spread, Is.EqualTo(plain).Within(0.01));
    }

    [Test]
    public void ADropCap_TakesNoRuby()
    {
        var layout = LayIn(NotoSansCjk, new AttributedText(Text + Text + Text).SetRuby(0, 1, new RubyAnnotation("に")),
            150, setUp: set => set.DropCap = new DropCap(2));

        Assume.That(layout.GetTextData().Single(g => g.PositionInString == 0).FontSize, Is.GreaterThan(FontSize));
        Assert.That(Annotation(layout), Is.Empty);
    }

    [Test]
    public void ABaseTheEllipsisCut_HasNoAnnotation()
    {
        var layout = LayIn(NotoSansCjk, new AttributedText(Text).SetRuby(Base, 2, new RubyAnnotation("かんじ")), 4 * FontSize,
            TextWrapping.NoWrap, trimming: TextTrimming.CharEllipses);

        Assert.That(Annotation(layout).Where(glyph => "かんじ".Contains(glyph.Symbol)), Is.Empty);
    }

    [Test]
    public void InAFixedHeight_EveryLineKeptStartsInside()
    {
        var text = new AttributedText(Text + Text + Text + Text);
        for (var i = 0; i < 4; i++)
        {
            text.SetRuby(i * Text.Length + Base, 2, new RubyAnnotation("かんじ"));
        }

        var layout = LayIn(NotoSansCjk, text, 8 * FontSize, height: 90, trimming: TextTrimming.CharEllipses);
        var tops = Enumerable.Range(0, layout.LineCount).Select(i => layout.GetLine(i).Top).ToArray();

        Assert.That(tops.All(top => top < 90), Is.True, string.Join(", ", tops));
    }

    [Test]
    public void TheAnnotation_FollowsItsBase_SoAnEffectOverTheBaseReachesIt()
    {
        var layout = Lay("かんじ");
        var glyphs = layout.GetTextData();
        var last = Array.FindLastIndex(glyphs, glyph => glyph.PositionInString == Base + 1);

        Assert.That(glyphs.Skip(last + 1).Take(3).All(glyph => glyph.PositionInString < 0), Is.True);
    }

    [Test]
    public void AnAnnotationTheBaseFontLacks_TakesTheFallbacksFont()
    {
        var latin = SourceSans.Value.Fonts[0];
        Assume.That(FontFallback.System.FontFor('と', latin, "ja"), Is.Not.Null, "a system font with kana");
        var layout = new TextLayout(SourceSans.Value, latin) { Fallback = FontFallback.System, LoadFontsInBackground = false };
        layout.ProcessText(new AttributedText("Tokyo").SetRuby(0, 5, new RubyAnnotation("とうきょう")), FontSize,
            new Size(400, double.NaN), TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Left,
            VerticalTextAlignment.Top);

        Assert.That(Annotation(layout).All(glyph => glyph.Glyph.Index != 0 && !ReferenceEquals(glyph.Font, latin)), Is.True);
    }

    [Test]
    public void ABaseInsideALigature_StillTakesItsAnnotation()
    {
        var playfair = new Lazy<Typeface>(() =>
            Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "PlayfairDisplay-Regular.ttf")));
        var layout = LayIn(playfair, new AttributedText("a fit b").SetRuby(3, 1, new RubyAnnotation("i")), 400);

        Assert.That(Annotation(layout), Is.Not.Empty);
    }

    [Test]
    public void InVerticalText_ALineOverTheBase_StaysOnItsText()
    {
        TextLayout Vertical(bool ruby)
        {
            var typeface = NotoSansCjk.Value;
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, WritingMode = WritingMode.VerticalRightToLeft,
            };
            var attributed = new AttributedText(Text).Apply(0, Text.Length,
                new TextAttributes { Decorations = TextDecorations.Strikethrough });
            if (ruby)
            {
                attributed.SetRuby(Base, 2, new RubyAnnotation("かんじ"));
            }

            layout.ProcessText(attributed, FontSize, new Size(200, 400), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout;
        }

        double Offset(TextLayout layout)
        {
            var line = layout.GetAdornments().First(adornment => adornment.Kind != TextAdornmentKind.Background).Rect;
            var bases = layout.GetTextData().Where(glyph => glyph.PositionInString >= 0).ToArray();
            var middle = (bases.Min(glyph => glyph.Rect.Left) + bases.Max(glyph => glyph.Rect.Right)) / 2;
            return line.Left + line.Width / 2 - middle;
        }

        Assert.That(Offset(Vertical(true)), Is.EqualTo(Offset(Vertical(false))).Within(0.5));
    }

    [Test]
    public void ABase_TakesWholeCharacters()
    {
        var text = new AttributedText("aéb").SetRuby(1, 1, new RubyAnnotation("x"));

        Assert.That((text.Rubies[0].Start, text.Rubies[0].Length), Is.EqualTo((1, 2)));
    }

    [Test]
    public void OnABaselineGrid_InAFixedHeight_EveryLineKeptStartsInside()
    {
        var text = new AttributedText(Text + Text + Text + Text);
        for (var i = 0; i < 4; i++)
        {
            text.SetRuby(i * Text.Length + Base, 2, new RubyAnnotation("かんじ"));
        }

        var layout = LayIn(NotoSansCjk, text, 8 * FontSize, height: 100, trimming: TextTrimming.CharEllipses,
            setUp: set => set.BaselineGrid = new BaselineGrid(24));
        var tops = Enumerable.Range(0, layout.LineCount).Select(i => layout.GetLine(i).Top).ToArray();

        Assert.That(tops.All(top => top < 100), Is.True, string.Join(", ", tops));
    }

    [Test]
    public void ADropCapThatDoesNotFit_LeavesTheRubyThere()
    {
        var layout = LayIn(NotoSansCjk, new AttributedText(Text).SetRuby(0, 2, new RubyAnnotation("にほん")), 60,
            setUp: set => set.DropCap = new DropCap(2));

        Assert.That(Annotation(layout), Is.Not.Empty);
    }

    [Test]
    public void ARubyStartingInADropCap_KeepsTheRestOfItsBase()
    {
        var layout = LayIn(NotoSansCjk, new AttributedText(Text + Text + Text).SetRuby(0, 2, new RubyAnnotation("ほん")),
            200, setUp: set => set.DropCap = new DropCap(2));
        var second = layout.GetTextData().Single(glyph => glyph.PositionInString == 1);
        var annotation = Annotation(layout);

        Assert.Multiple(() =>
        {
            Assert.That(annotation, Is.Not.Empty);
            Assert.That((Left(annotation) + Right(annotation)) / 2, Is.EqualTo(second.PenX + second.Advance / 2).Within(0.01));
        });
    }

    [Test]
    public void ATabInABase_StillReachesItsStop()
    {
        var plain = LayIn(SourceSans, new AttributedText("ab\tcd"), 400);
        var layout = LayIn(SourceSans, new AttributedText("ab\tcd").SetRuby(0, 5, new RubyAnnotation("x")), 400);

        Assert.That(layout.GetTextData().Single(glyph => glyph.PositionInString == 3).PenX,
            Is.EqualTo(plain.GetTextData().Single(glyph => glyph.PositionInString == 3).PenX).Within(0.01));
    }

    [Test]
    public void ATextEndingInASpreadBase_IsAsWideAsItsAnnotation()
    {
        var text = new AttributedText("a").SetRuby(0, 1, new RubyAnnotation("averyveryverylongannotation"));
        var layout = new TextLayout(SourceSans.Value, SourceSans.Value.Fonts[0]) { Fallback = null };
        var size = layout.ProcessText(text, FontSize, new Size(double.NaN, double.NaN), TextWrapping.NoWrap,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(size.Width, Is.GreaterThanOrEqualTo(Right(Annotation(layout)) - 0.5));
    }

    [Test]
    public void AlignedRight_ASpreadBaseEndsAtTheEdge()
    {
        var text = new AttributedText("x a").SetRuby(2, 1, new RubyAnnotation("averyveryverylongannotation"));
        var layout = new TextLayout(SourceSans.Value, SourceSans.Value.Fonts[0]) { Fallback = null };
        layout.ProcessText(text, FontSize, new Size(300, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Right, VerticalTextAlignment.Top);

        Assert.That(Right(Annotation(layout)), Is.EqualTo(300).Within(0.5));
    }

    [Test]
    public void ABaseNotSpread_LeavesItsLineAlignedByItsInk()
    {
        TextLayout Right(bool ruby)
        {
            var text = new AttributedText("hello world");
            if (ruby)
            {
                text.SetRuby(6, 5, new RubyAnnotation("w"));
            }

            var layout = new TextLayout(SourceSans.Value, SourceSans.Value.Fonts[0]) { Fallback = null, Tracking = 100 };
            layout.ProcessText(text, FontSize, new Size(300, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Right, VerticalTextAlignment.Top);
            return layout;
        }

        double InkRight(TextLayout layout) => layout.GetTextData().Where(glyph => glyph.PositionInString >= 0)
            .Max(glyph => glyph.Rect.Right);

        Assert.That(InkRight(Right(true)), Is.EqualTo(InkRight(Right(false))).Within(0.01));
    }

    [Test]
    public void InVerticalText_TheInkMeasuresAcrossTheColumns()
    {
        TextLayout Vertical(bool ruby)
        {
            var typeface = NotoSansCjk.Value;
            var layout = new TextLayout(typeface, typeface.Fonts[0])
            {
                Fallback = null, WritingMode = WritingMode.VerticalRightToLeft,
            };
            var attributed = new AttributedText(Text);
            if (ruby)
            {
                attributed.SetRuby(Base, 2, new RubyAnnotation("とてもながいよみがな"));
            }

            layout.ProcessText(attributed, FontSize, new Size(200, 400), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout;
        }

        var layout = Vertical(true);
        var glyphs = layout.GetTextData();
        var inkWidth = glyphs.Max(glyph => glyph.Rect.Right) - glyphs.Min(glyph => glyph.Rect.Left);

        Assert.That(layout.RealTextDimensions.Width, Is.EqualTo(inkWidth).Within(0.5));
    }

    [Test]
    public void ABaseTheEllipsisCut_DoesNotGrowItsLine()
    {
        var plain = LayIn(NotoSansCjk, new AttributedText(Text), 4 * FontSize, TextWrapping.NoWrap,
            trimming: TextTrimming.CharEllipses).GetLine(0);
        var line = LayIn(NotoSansCjk, new AttributedText(Text).SetRuby(Base, 2, new RubyAnnotation("かんじ")),
            4 * FontSize, TextWrapping.NoWrap, trimming: TextTrimming.CharEllipses).GetLine(0);

        Assert.That(line.Height, Is.EqualTo(plain.Height).Within(0.01));
    }

    [Test]
    public void SettingARuby_ReplacesTheOnesItOverlaps_AndAnEmptyOneRemovesThem()
    {
        var text = new AttributedText(Text)
            .SetRuby(0, 2, new RubyAnnotation("にほん"))
            .SetRuby(3, 2, new RubyAnnotation("かんじ"))
            .SetRuby(1, 3, new RubyAnnotation("ほんのか"));

        Assert.That(text.Rubies.Select(run => (run.Start, run.Length, run.Annotation.Text)), Is.EqualTo(new[] { (1, 3, "ほんのか") }));
        text.SetRuby(0, Text.Length, new RubyAnnotation(string.Empty));
        Assert.That(text.Rubies, Is.Empty);
    }

    private static TextLayout LayDoubleSided(int overLength, string over, string under)
    {
        var typeface = NotoSansCjk.Value;
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        var attributed = new AttributedText(Text);
        attributed.SetRuby(Base, overLength, new RubyAnnotation(over));
        attributed.SetRuby(Base, 2, new RubyAnnotation(under, RubyPosition.Under));
        layout.ProcessText(attributed, FontSize, new Size(400, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    // A base takes one annotation over it and one under it: the second does not take the first one's place.
    [TestCase(2)]
    [TestCase(1)]
    public void ADoubleSidedRuby_ShowsBothAnnotations(int overLength)
    {
        var layout = LayDoubleSided(overLength, "かん", "kanji");
        var annotation = Annotation(layout);
        var bases = BaseGlyphs(layout);
        var top = bases.Min(glyph => glyph.Rect.Top);
        var bottom = bases.Max(glyph => glyph.Rect.Bottom);

        Assert.Multiple(() =>
        {
            Assert.That(layout.AttributedText.Rubies, Has.Count.EqualTo(2));
            Assert.That(annotation.Count(glyph => glyph.Rect.Bottom <= top + 0.5), Is.EqualTo(2), "the reading over it");
            Assert.That(annotation.Count(glyph => glyph.Rect.Top >= bottom - 0.5), Is.EqualTo(5), "the gloss under it");
        });
    }

    [Test]
    public void ARubyOnTheSameSide_StillTakesThePlaceOfTheOneItOverlaps()
    {
        var text = new AttributedText(Text);
        text.SetRuby(0, 2, new RubyAnnotation("にほん"));
        text.SetRuby(1, 2, new RubyAnnotation("ほんの"));

        Assert.That(text.Rubies.Select(run => run.Annotation.Text), Is.EqualTo(new[] { "ほんの" }));
    }

    // No annotation clears a range of the rubies on both sides; an empty one only those on its own.
    [Test]
    public void NoAnnotation_ClearsBothSides_AnEmptyOneItsOwn()
    {
        var text = new AttributedText(Text);
        text.SetRuby(0, 2, new RubyAnnotation("にほん"));
        text.SetRuby(0, 2, new RubyAnnotation("nihon", Position: RubyPosition.Under));
        text.SetRuby(0, 5, new RubyAnnotation(""));

        Assert.That(text.Rubies.Select(run => run.Annotation.Text), Is.EqualTo(new[] { "nihon" }));

        text.SetRuby(0, 5, null);
        Assert.That(text.Rubies, Is.Empty);
    }

    // A base spread to its annotation does not hang its punctuation past the margin: the annotation would go with it.
    [Test]
    public void ASpreadBaseOpeningALine_DoesNotHangItsAnnotationPastTheMargin()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, OpticalMarginAlignment = true };
        var attributed = new AttributedText("“Ab cd");
        attributed.SetRuby(0, 3, new RubyAnnotation("annotation", FontSize: 20));
        layout.ProcessText(attributed, 40, new Size(400, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(Annotation(layout).Min(glyph => glyph.Rect.Left), Is.GreaterThanOrEqualTo(-0.5));
    }

    // Shaping is cut at a base's edge; the pair across it keeps the kerning the font gives it.
    [Test]
    public void APairAcrossABasesEdge_KeepsItsKerning()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        TextLayout LayAva(bool ruby)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
            var attributed = new AttributedText("AVA");
            if (ruby)
            {
                attributed.SetRuby(1, 1, new RubyAnnotation("v", FontSize: 4));
            }

            layout.ProcessText(attributed, 40, new Size(400, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout;
        }

        var plain = LayAva(false).GetTextData().Where(glyph => glyph.PositionInString >= 0).ToArray();
        var annotated = LayAva(true).GetTextData().Where(glyph => glyph.PositionInString >= 0).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(annotated[1].PenX, Is.EqualTo(plain[1].PenX).Within(0.01), "A and V kerned");
            Assert.That(annotated[2].PenX, Is.EqualTo(plain[2].PenX).Within(0.01), "V and A kerned");
        });
    }

    // Right to left too, a narrow annotation leaves its base's letters where they stand, kerned as without it.
    [TestCase(1)]
    [TestCase(2)]
    public void ANarrowAnnotation_RightToLeft_MovesNoLetter(int at)
    {
        const string text = "שלום";
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "NotoSansHebrew-Regular.ttf"));
        GlyphWordData[] Letters(bool ruby)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
            var attributed = new AttributedText(text);
            if (ruby)
            {
                attributed.SetRuby(at, 1, new RubyAnnotation("x", FontSize: 2));
            }

            layout.ProcessText(attributed, 40, new Size(400, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
            return layout.GetTextData().Where(glyph => glyph.PositionInString >= 0).ToArray();
        }

        var plain = Letters(false);
        var annotated = Letters(true);

        Assert.That(annotated.Select(glyph => glyph.Advance), Is.EqualTo(plain.Select(glyph => glyph.Advance)).Within(0.01));
        Assert.That(annotated.Select(glyph => glyph.PenX + glyph.OffsetX),
            Is.EqualTo(plain.Select(glyph => glyph.PenX + glyph.OffsetX)).Within(0.01));
    }

    // A box trimmed to the capitals keeps an annotation over the first line inside it, and is trimmed as without one
    // when the annotation is under the line.
    [Test]
    public void TrimToCapHeight_KeepsAnAnnotationOverTheFirstLineInside()
    {
        var layout = Lay("にほん", setUp: l => l.LineBounds = TextLineBounds.TrimToCapHeight, start: 0);

        Assert.That(Annotation(layout).Min(glyph => glyph.Rect.Y), Is.GreaterThanOrEqualTo(-1));
    }

    [Test]
    public void TrimToCapHeight_TrimsALineWhoseAnnotationIsUnder()
    {
        var plain = Lay(null, setUp: l => l.LineBounds = TextLineBounds.TrimToCapHeight);
        var under = Lay("にほん", RubyPosition.Under, setUp: l => l.LineBounds = TextLineBounds.TrimToCapHeight, start: 0);

        Assert.That(under.GetLine(0).Baseline, Is.EqualTo(plain.GetLine(0).Baseline));
    }
}
