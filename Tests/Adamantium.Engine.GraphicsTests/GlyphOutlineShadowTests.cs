using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// A text's outline, shadow, glow and reflection: copies of its glyphs from the same field, all under the text, each
// grown and softened no further than the field reaches.
[TestFixture]
public class GlyphOutlineShadowTests
{
    private const float FontSize = 40;

    [TearDown]
    public void ReleaseDevices()
    {
        FontAtlasStore.SynchronousFill = false;
        FontAtlasStore.Reset();
        GpuFixture.ReleaseRenderDevices();
    }

    [Test]
    public void EachEffect_IsACopyOfEveryGlyph_UnderAllOfTheText_InItsOwnColor()
    {
        var plain = Run(null, null);
        var effects = Run(null, layout =>
        {
            layout.GlyphOutline = new GlyphOutline(Colors.Red, 2);
            layout.GlyphShadow = new GlyphShadow(Colors.Black, 3, 4, 2);
            layout.GlyphGlow = new GlyphGlow(Colors.Yellow, 3);
            layout.GlyphReflection = new GlyphReflection(0.5, 2, 0.6);
        });
        var glyphs = plain.Count;
        var items = effects.Glyphs.Take(effects.Count).ToArray();
        var reflections = items[..glyphs];
        var shadows = items[glyphs..(2 * glyphs)];
        var glows = items[(2 * glyphs)..(3 * glyphs)];
        var outlines = items[(3 * glyphs)..(4 * glyphs)];
        var text = items[(4 * glyphs)..];

        Assert.Multiple(() =>
        {
            Assert.That(effects.Count, Is.EqualTo(5 * glyphs));
            Assert.That(text.Select(g => g.ArrangeRect), Is.EqualTo(plain.Glyphs.Take(glyphs).Select(g => g.ArrangeRect)),
                "the text last, over its effects, as it was");
            Assert.That(shadows.Select(g => g.ArrangeRect.X - 3), Is.EqualTo(text.Select(g => g.ArrangeRect.X)).Within(1e-4));
            Assert.That(shadows.All(g => g.Synthesis.W > 0 && g.Color == Colors.Black.ToVector4() && g.Place.Y == 0));
            Assert.That(glows.All(g => g.Synthesis.X > 0 && g.Synthesis.W > 0));
            Assert.That(outlines.All(g => g.Synthesis.X > 0 && g.Synthesis.W == 0));
            Assert.That(reflections.All(g => g.Source.W < 0 && g.Paint.Z > 0 && g.Paint.W == 0.5f));
            Assert.That(reflections.Zip(text).All(pair => pair.First.ArrangeRect.Y > pair.Second.ArrangeRect.Y),
                "below the line");
        });
    }

    // However wide asked, an effect grows and softens no further than the field reaches past the outline.
    [Test]
    public void AWideEffect_StaysWithinTheField()
    {
        var run = Run(null, layout =>
        {
            layout.GlyphGlow = new GlyphGlow(Colors.Yellow, 500);
            layout.GlyphOutline = new GlyphOutline(Colors.Red, 500);
        });
        var items = run.Glyphs.Take(run.Count).ToArray();

        Assert.That(items.Where(g => g.Place.Y == 0).All(g => g.Synthesis.X + g.Synthesis.W / 2 <= 0.5f + 1e-4f));
    }

    // A stretch's own effect over the layout's: only its glyphs take it.
    [Test]
    public void AStretchsEffect_IsItsOwn()
    {
        var attributed = new AttributedText("ab cd")
            .Apply(3, 2, new TextAttributes { Shadow = new GlyphShadow(Colors.Black, 2, 2, 0) });
        var run = Run(attributed, null);
        var shadows = run.Glyphs.Take(run.Count).Where(g => g.Place.Y == 0).Select(g => (int)g.Place.X).ToArray();

        Assert.That(shadows, Is.EqualTo(new[] { 3, 4 }));
    }

    private static FrozenGlyphRun Run(AttributedText attributed, System.Action<TextLayout> setUp)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null };
        setUp?.Invoke(layout);
        if (attributed != null)
        {
            layout.ProcessText(attributed, FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap,
                TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        }
        else
        {
            layout.ProcessText("Hgy", FontSize, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
                HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        }

        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        return layout.BuildGlyphRun(device, null);
    }
}
