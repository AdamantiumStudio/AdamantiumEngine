using System;
using System.IO;
using System.Linq;
using System.Threading;
using Adamantium.Fonts;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>The images of color glyphs past <see cref="FontAtlasStore.ColorImageLayerBudget"/>: the ones no text draws
/// any longer give their cells to new ones, the ones text still draws are kept, and one let go comes back where it was
/// drawn.</summary>
[TestFixture]
public class ColorImageEvictionTests
{
    // Noto Color Emoji's images are 136 by 128 pixels: cells of 256 by 128, 32 to a layer.
    private const int CellsInALayer = ColorBitmapAtlas.LayerSize / 256 * (ColorBitmapAtlas.LayerSize / 128);

    [TearDown]
    public void ReleaseDevices()
    {
        FontAtlasStore.ColorImageLayerBudget = 16;
        FontAtlasStore.Reset();
        GpuFixture.ReleaseRenderDevices();
    }

    private static TextLayout Layout(string text)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "NotoColorEmoji.subset.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText(text, 20, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static ColorBitmapImage Blank(int cellWidth, int cellHeight)
    {
        var bytes = 0;
        for (var level = 0; (cellWidth >> level) > 0 && (cellHeight >> level) > 0; level++)
        {
            bytes += (cellWidth >> level) * (cellHeight >> level) * 4;
        }

        return new ColorBitmapImage(cellWidth, cellHeight, cellWidth, cellHeight, new byte[bytes]);
    }

    // Pumps the atlas a frame at a time, the drawn blocks building again as glyphs land, until nothing is on its way.
    private static void Draw(FontAtlas atlas, IGraphicsDevice device, params TextLayout[] drawn)
    {
        foreach (var layout in drawn)
        {
            layout.Update(device);
        }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            Thread.Sleep(1);
            atlas.PumpReady();
            foreach (var layout in drawn.Where(layout => layout.NeedsGlyphRefresh))
            {
                layout.RefreshGlyphs(device);
            }
        } while (atlas.HasPendingGlyphs && DateTime.UtcNow < deadline);

        Assert.That(atlas.HasPendingGlyphs, Is.False, "every image has landed");
    }

    private static float[] Programs(TextLayout layout) =>
        layout.SnapshotGlyphs().Glyphs.Select(glyph => glyph.Paint.X - 1).ToArray();

    private static FontAtlas FilledButTwoCells(IGraphicsDevice device)
    {
        FontAtlasStore.ColorImageLayerBudget = 1;
        var atlas = Layout("8").EnsureAtlas(device);
        for (var i = 0; i < CellsInALayer - 2; i++)
        {
            atlas.ColorImages.Add(Blank(256, 128), false);
        }

        return atlas;
    }

    [Test]
    public void AnImageNoTextDraws_GivesItsCellToOneThatIsDrawn()
    {
        var device = GpuFixture.CreateRenderDevice();
        var atlas = FilledButTwoCells(device);
        var gone = Layout("89");
        Draw(atlas, device, gone);
        Assume.That(Programs(gone), Has.Length.EqualTo(2).And.All.GreaterThanOrEqualTo(0));

        var shown = Layout("®");
        Draw(atlas, device, shown);

        Assert.That(Programs(shown), Has.Length.EqualTo(1).And.All.GreaterThanOrEqualTo(0), "the new image is drawn");
        Assert.That(atlas.ColorImages.LayersTaken, Is.EqualTo(1), "in the cell of one no text draws");
    }

    [Test]
    public void ImagesTextStillDraws_AreKept_AndTheAtlasGrowsPastItsBudget()
    {
        var device = GpuFixture.CreateRenderDevice();
        var atlas = FilledButTwoCells(device);
        var kept = Layout("89");
        Draw(atlas, device, kept);
        var before = Programs(kept);

        var shown = Layout("®");
        Draw(atlas, device, kept, shown);

        Assert.Multiple(() =>
        {
            Assert.That(Programs(kept), Is.EqualTo(before), "the drawn images stay where they are");
            Assert.That(Programs(shown), Has.Length.EqualTo(1).And.All.GreaterThanOrEqualTo(0), "the new one is drawn");
            Assert.That(atlas.ColorImages.LayersTaken, Is.EqualTo(2), "on a layer past the budget");
        });
    }

    [Test]
    public void AnImageLetGo_ComesBack_UnderTheProgramItWasDrawnWith()
    {
        var device = GpuFixture.CreateRenderDevice();
        var atlas = FilledButTwoCells(device);
        var back = Layout("89");
        Draw(atlas, device, back);
        var before = Programs(back);
        Draw(atlas, device, Layout("®"));

        Draw(atlas, device, back);

        Assert.That(Programs(back), Is.EqualTo(before));
        Assert.That(atlas.ColorImages.LayersTaken, Is.EqualTo(1));
    }

    [Test]
    public void ALayerGivenBackWhole_TakesImagesOfAnotherSize()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.ColorImageLayerBudget = 1;
        using var images = new ColorBitmapAtlas(device);
        var large = Enumerable.Range(0, 16).Select(_ => images.Add(Blank(256, 256), false)).ToList();
        Assume.That(large, Has.None.Null);
        Assume.That(images.Add(Blank(32, 32), false), Is.Null, "the one layer is taken");

        foreach (var cell in large)
        {
            images.Free(cell);
        }

        var small = images.Add(Blank(32, 32), false);

        Assert.That(small, Is.Not.Null);
        Assert.That(small.Layer, Is.EqualTo(0));
    }

    [Test]
    public void ACellGivenBack_TakesTheNextImageOfItsSize()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.ColorImageLayerBudget = 1;
        using var images = new ColorBitmapAtlas(device);
        var cells = Enumerable.Range(0, 16).Select(_ => images.Add(Blank(256, 256), false)).ToList();

        images.Free(cells[5]);
        var next = images.Add(Blank(256, 256), false);

        Assert.That((next.Layer, next.X, next.Y), Is.EqualTo((cells[5].Layer, cells[5].X, cells[5].Y)));
        Assert.That(images.Add(Blank(256, 256), false), Is.Null, "no other room under the budget");
        Assert.That(images.Add(Blank(256, 256), true), Is.Not.Null, "past it, a layer more");
    }
}
