using System;
using System.IO;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The settings a layout composes lines by, as one value: what a control lays text out with and compares to
/// tell whether to lay it out again.</summary>
[TestFixture]
public class TextCompositionTests
{
    [Test]
    public void TwoCompositions_OfTheSameSettings_AreEqual()
    {
        var one = new TextComposition { Hyphens = Hyphens.Auto, WordSpacing = new SpacingRange(0.7, 1, 1.5) };
        var other = new TextComposition { Hyphens = Hyphens.Auto, WordSpacing = new SpacingRange(0.7, 1, 1.5) };

        Assert.That(one, Is.EqualTo(other));
        Assert.That(one.GetHashCode(), Is.EqualTo(other.GetHashCode()));
        Assert.That(one with { Kashidas = false }, Is.Not.EqualTo(other));
        Assert.That(new TextComposition(), Is.EqualTo(TextComposition.Default));
    }

    [Test]
    public void ACompositionRefuses_GlyphsScaledToNothing_AndANegativeHyphenLimit()
    {
        Assert.That(() => new TextComposition { GlyphScaling = new SpacingRange(0, 1, 1) },
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => TextComposition.Default with { HyphenLimit = -1 }, Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(TextComposition.ScalesGlyphs(new SpacingRange(0, 1, 1)), Is.False);
        Assert.That(TextComposition.ScalesGlyphs(new SpacingRange(0.9, 1, 1.1)), Is.True);
    }

    [Test]
    public void ALayout_ReadsAndWritesItsSettings_ThroughItsComposition()
    {
        var layout = Layout();
        layout.Hyphens = Hyphens.Auto;
        layout.GlyphScaling = new SpacingRange(0.9, 1, 1.1);
        Assert.That(layout.Composition,
            Is.EqualTo(new TextComposition { Hyphens = Hyphens.Auto, GlyphScaling = new SpacingRange(0.9, 1, 1.1) }));

        layout.Composition = new TextComposition { LineBreaking = LineBreaking.Paragraph };
        Assert.That(layout.Hyphens, Is.EqualTo(Hyphens.Manual), "the whole composition is replaced");
        Assert.That(layout.LineBreaking, Is.EqualTo(LineBreaking.Paragraph));
        Assert.That(() => layout.Composition = null, Throws.TypeOf<ArgumentNullException>());
        Assert.That(() => layout.GlyphScaling = new SpacingRange(0, 1, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ANewComposition_LaysTheTextOutAgain_AnEqualOneDoesNot()
    {
        var layout = Layout();

        Size Lay() => layout.ProcessText("one two three four", 20, new Size(80, double.NaN), TextWrapping.WrapByWords,
            TextTrimming.None, HorizontalTextAlignment.Justify, VerticalTextAlignment.Top);

        layout.Composition = new TextComposition { LastLineAlignment = HorizontalTextAlignment.Center };
        Lay();
        var first = layout.Revision;
        layout.Composition = new TextComposition { LastLineAlignment = HorizontalTextAlignment.Center };
        Lay();
        Assert.That(layout.Revision, Is.EqualTo(first), "an equal composition changes nothing");

        layout.Composition = layout.Composition with { LastLineAlignment = HorizontalTextAlignment.Right };
        Lay();
        Assert.That(layout.Revision, Is.GreaterThan(first));
    }

    private static TextLayout Layout()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "SourceSans3-Regular.ttf"));
        return new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
    }
}
