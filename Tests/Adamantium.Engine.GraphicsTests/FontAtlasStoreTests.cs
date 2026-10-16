using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

[TestFixture]
public class FontAtlasStoreTests
{
    [TearDown]
    public void ReleaseDevices()
    {
        FontAtlasStore.SynchronousFill = false;
        FontAtlasStore.Reset();
        GpuFixture.ReleaseRenderDevices();
    }

    private static TextLayout Layout(string file, string text)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", file));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText(text, 20, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    [Test]
    public void AnAtlasGrows_AsItsGlyphsFillIt()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout("SourceSans3-Regular.ttf", "a");
        var atlas = layout.EnsureAtlas(device);
        var font = layout.Font;
        var glyphs = font.Typeface.Glyphs
            .Where(g => !g.IsEmpty && g.BoundingRectangle.Width > 0 && g.BoundingRectangle.Height > 0)
            .Take(600)
            .Select(g => (font, g))
            .ToList();

        atlas.RequestAsync(glyphs);

        Assert.That(atlas.LayerCount, Is.GreaterThan(FontAtlas.InitialLayerCount), "600 glyphs need more than two layers");
        Assert.That(atlas.LayerCapacity, Is.GreaterThanOrEqualTo(atlas.LayerCount));
        Assert.That(glyphs.All(p => atlas.GetGlyphData(p.font, p.g) != null), "every glyph has its cell");
    }

    [Test]
    public void TwoFonts_ShareOneAtlas_AndKeepTheirGlyphsApart()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var regular = Layout("SourceSans3-Regular.ttf", "a");
        var bold = Layout("SourceSans3-Bold.ttf", "a");
        regular.Update(device);
        bold.Update(device);

        var regularA = regular.GetTextData().Single();
        var boldA = bold.GetTextData().Single();
        var atlas = regular.FontAtlas;
        var regularCell = atlas.GetGlyphData(regularA.Font, regularA.Glyph);
        var boldCell = atlas.GetGlyphData(boldA.Font, boldA.Glyph);

        Assert.That(bold.FontAtlas, Is.SameAs(atlas), "one atlas for every font");
        Assert.That(boldA.Glyph.Index, Is.EqualTo(regularA.Glyph.Index), "the same index in both fonts");
        Assert.That(regularCell, Is.Not.Null);
        Assert.That(boldCell, Is.Not.Null);
        Assert.That(boldCell, Is.Not.SameAs(regularCell), "a glyph of one font never draws the other's picture");
    }

    [Test]
    public void ABlockOfMoreThan4096Glyphs_DrawsThemAll()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout("SourceSans3-Regular.ttf", new string('a', 5000));

        layout.Update(device);

        Assert.That(layout.ElementsCount, Is.EqualTo(5000), "no glyph past a fixed cap is dropped");
    }

    // A weight animation's frame lays the text out at its own weight and draws each glyph from the two key instances
    // around it: they go into the atlas, the frame's own glyphs never do.
    [Test]
    public void AGlyphOnTheWayBetweenTwoWeights_IsDrawnFromBothKeyInstances()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "RobotoFlex-Variable.ttf"));
        var moving = typeface.Fonts[0].GetInstance([new FontVariation("wght", 400)], [new FontVariation("wght", 700)], 0.42f);
        var layout = new TextLayout(moving.Typeface, moving);
        layout.ProcessText("m", 40, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        layout.Update(device);
        var run = layout.SnapshotGlyphs();
        var word = layout.GetTextData().Single();
        var blend = word.Font.Blend;
        var atlas = layout.FontAtlas;
        var item = run.Glyphs[0];

        Assert.That(run.Count, Is.EqualTo(1));
        Assert.That(blend, Is.Not.Null, "at the text's optical size, still on the way");
        Assert.Multiple(() =>
        {
            Assert.That(atlas.GetGlyphData(blend.From, blend.From.GetGlyphByIndex(word.Glyph.Index)), Is.Not.Null);
            Assert.That(atlas.GetGlyphData(blend.To, blend.To.GetGlyphByIndex(word.Glyph.Index)), Is.Not.Null);
            Assert.That(atlas.GetGlyphData(word.Font, word.Glyph), Is.Null, "the frame's own glyph");
            Assert.That(item.SecondSource.Z, Is.GreaterThan(0), "the second key's cell");
            Assert.That(item.Second.Y, Is.EqualTo(blend.Amount).Within(1e-6));
            Assert.That(item.Source.Z, Is.GreaterThan(0));
        });
    }

    [Test]
    public void AColorGlyph_DrawsOneQuadThatRunsItsPaintProgram()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "test_glyphs-glyf_colr_1.ttf"));
        var font = typeface.Fonts[0];
        var codepoints = font.Unicodes
            .Where(u => font.TryGetGlyphIndex((int)u, out var g) &&
                        font.GetColorPaint(g).Count(o => o.Kind == ColorPaintOperationKind.PushClip) > 1)
            .Take(2)
            .ToArray();
        var layout = new TextLayout(typeface, font);
        layout.ProcessText(string.Concat(codepoints.Select(c => char.ConvertFromUtf32((int)c))), 20,
            new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None, HorizontalTextAlignment.Left,
            VerticalTextAlignment.Top);

        layout.Update(device);
        var run = layout.SnapshotGlyphs();

        Assert.That(run.Count, Is.EqualTo(2), "a quad per color glyph, however many outlines it clips to");
        Assert.That(run.Glyphs.Take(run.Count).All(g => g.Paint.X >= 1), "each quad names its paint program");
        Assert.That(run.Glyphs[0].Paint.X, Is.Not.EqualTo(run.Glyphs[1].Paint.X), "each glyph has a program of its own");
    }

    [Test]
    public void AColorGlyph_InAnotherPalette_RunsAProgramOfItsOwn()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "test_glyphs-glyf_colr_1.ttf"));
        var text = char.ConvertFromUtf32(0xF0100);

        float Program(int palette)
        {
            var layout = new TextLayout(typeface, typeface.Fonts[0]);
            layout.ProcessText(new AttributedText(text, new TextAttributes { ColorPalette = palette }), 20,
                new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None, HorizontalTextAlignment.Left,
                VerticalTextAlignment.Top);
            layout.Update(device);
            return layout.SnapshotGlyphs().Glyphs[0].Paint.X;
        }

        Assert.That(Program(1), Is.Not.EqualTo(Program(0)), "the palette's colors are a program of their own");
        Assert.That(Program(1), Is.EqualTo(Program(1)), "and the same palette asks for the same program");
    }

    [Test]
    public void AColorGlyphOfLayers_DrawsAQuadPerLayerInItsPaletteColor()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout("chromacheck-colr.ttf", "\U0000E900");
        var font = layout.Font;
        font.TryGetGlyphIndex(0xE900, out var glyph);
        var layers = font.GetColorLayers(glyph);

        layout.Update(device);
        var run = layout.SnapshotGlyphs();

        Assert.That(layers, Is.Not.Empty, "the glyph has 'COLR' version 0 layers");
        Assert.That(run.Count, Is.EqualTo(layers.Count), "a quad per layer");
        for (var i = 0; i < layers.Count; i++)
        {
            Assert.That(run.Glyphs[i].Paint.X, Is.Zero, "a layer is an ordinary glyph, not a paint program");
            Assert.That(run.Glyphs[i].Color, Is.EqualTo(layers[i].Color.Value.ToVector4()), "in the palette's color");
        }
    }

    [Test]
    public void AGlyphFromAnImage_DrawsOneQuadThatRunsItsProgram()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout("NotoColorEmoji.subset.ttf", "\U00002049\U00002049");

        layout.Update(device);
        var run = layout.SnapshotGlyphs();

        Assert.That(run.Count, Is.EqualTo(2), "a quad per glyph");
        Assert.That(run.Glyphs[0].Paint.X, Is.GreaterThanOrEqualTo(1), "the quad names the program that samples the image");
        Assert.That(run.Glyphs[1].Paint.X, Is.EqualTo(run.Glyphs[0].Paint.X), "the image is in the atlas once");
        Assert.That(layout.FontAtlas.HasPendingGlyphs, Is.False, "the image has landed");
    }
}
