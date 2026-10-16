using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// A long text shown through a viewport builds quads for what the viewport can show, not for every glyph it holds.
[TestFixture]
public class TextQuadWindowTests
{
    private const int Lines = 200;

    [TearDown]
    public void ReleaseDevices()
    {
        FontAtlasStore.SynchronousFill = false;
        FontAtlasStore.Reset();
        GpuFixture.ReleaseRenderDevices();
    }

    private static TextLayout Layout()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        var text = string.Join("\n", Enumerable.Range(0, Lines).Select(i => $"line number {i}"));
        layout.ProcessText(text, 20, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static RectangleF LinesWindow(TextLayout layout, int first, int last)
    {
        var top = layout.GetLine(first).Top;
        var bottom = layout.GetLine(last).Top + layout.GetLine(last).Height;
        return new RectangleF(0, (float)top, 2000, (float)(bottom - top));
    }

    private static int InkIn(TextLayout layout, RectangleF window) => layout.GetTextData().Count(g =>
        g.Symbol is not (' ' or '\t' or '\n')
        && g.Rect.Right >= window.Left && g.Rect.Left <= window.Right
        && g.Rect.Bottom >= window.Top && g.Rect.Top <= window.Bottom);

    [Test]
    public void AWindow_BuildsTheQuadsOfTheGlyphsInIt_AndNoOthers()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout();
        layout.Update(device);
        var all = layout.ElementsCount;
        var window = LinesWindow(layout, 50, 60);

        layout.Update(device, window);

        Assert.Multiple(() =>
        {
            Assert.That(layout.ElementsCount, Is.EqualTo(InkIn(layout, window)), "the glyphs that touch the window");
            Assert.That(layout.ElementsCount, Is.LessThan(all / 10), "a fraction of the whole text");
            Assert.That(layout.QuadWindow, Is.EqualTo(window));
            Assert.That(layout.SnapshotGlyphs().Count, Is.EqualTo(layout.ElementsCount), "the snapshot holds the same");
        });
    }

    [Test]
    public void GlyphsArrivingInTheAtlas_RebuildTheSameWindow()
    {
        var device = GpuFixture.CreateRenderDevice();
        var layout = Layout();
        var window = LinesWindow(layout, 120, 130);

        layout.Update(device, window);
        // Waited for, not switched to a synchronous fill: a glyph already on its way is not filled again, and a refresh
        // taken before it landed came out short by it.
        var deadline = System.DateTime.UtcNow.AddSeconds(30);
        while (FontAtlasStore.HasPendingGlyphs && System.DateTime.UtcNow < deadline)
        {
            FontAtlasStore.PumpReadyGlyphs();
            System.Threading.Thread.Sleep(5);
        }

        Assert.That(FontAtlasStore.HasPendingGlyphs, Is.False, "the glyphs never arrived");
        layout.RefreshGlyphs(device);

        Assert.Multiple(() =>
        {
            Assert.That(layout.QuadWindow, Is.EqualTo(window), "the window is kept");
            Assert.That(layout.ElementsCount, Is.EqualTo(InkIn(layout, window)));
        });
    }

    [Test]
    public void NoWindow_BuildsEveryGlyph()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout();
        var everything = new RectangleF(-1e6f, -1e6f, 2e6f, 2e6f);
        layout.Update(device, LinesWindow(layout, 0, 3));

        layout.Update(device, null);

        Assert.Multiple(() =>
        {
            Assert.That(layout.QuadWindow, Is.Null);
            Assert.That(layout.ElementsCount, Is.EqualTo(InkIn(layout, everything)));
        });
    }
}
