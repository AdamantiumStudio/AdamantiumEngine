using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// A block drawing a window of a layout draws the lines in it whole, and no glyph of a line outside it, however far its
// ink reaches.
[TestFixture]
public class WindowedGlyphRunTests
{
    [TearDown]
    public void ReleaseDevices()
    {
        FontAtlasStore.SynchronousFill = false;
        FontAtlasStore.Reset();
        GpuFixture.ReleaseRenderDevices();
    }

    [Test]
    public void AWindow_DrawsItsLinesWhole_AndNothingOfTheLinesAround()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, LineHeight = 20 };
        layout.ProcessText("gyp\nHlk\njqy", 40, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var middle = layout.GetLine(1);
        var inks = layout.GetTextData();
        Assume.That(inks.Where(g => g.LineIndex == 0).Max(g => g.Rect.Bottom), Is.GreaterThan(middle.Top + 1),
            "the first line's descenders reach into the second");
        Assume.That(inks.Where(g => g.LineIndex == 1).Min(g => g.Rect.Top), Is.LessThan(middle.Top - 1),
            "the second line's ascenders reach above it");
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;

        var run = layout.BuildGlyphRun(device, new RectangleF(-1000, (float)middle.Top, 4000, (float)middle.Height));
        var drawn = run.Glyphs.Take(run.Count).Select(g => (int)g.Place.X).OrderBy(p => p).ToArray();

        Assert.That(drawn, Is.EqualTo(new[] { 4, 5, 6 }));
    }

    // The drop cap of a text shorter than it stands below its lines: it is in a window there too.
    [Test]
    public void TheDropCapOfAShortText_IsInAWindowBelowItsLines()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, DropCap = new DropCap(3) };
        layout.ProcessText("In short", 20, new Size<double>(300, double.NaN), TextWrapping.WrapByWords, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var below = layout.GetLine(0).Top + layout.GetLine(0).Height + 2;
        var cap = layout.GetTextData().Single(g => g.PositionInString == 0);
        Assume.That(cap.Rect.Bottom, Is.GreaterThan(below + 10));
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;

        var run = layout.BuildGlyphRun(device, new RectangleF(-1000, (float)below, 4000, 200));

        Assert.That(run.Glyphs.Take(run.Count).Select(g => (int)g.Place.X), Does.Contain(0));
    }

    // A drop cap is in every line it spans.
    [Test]
    public void ADropCap_IsInAWindowOfAnyLineItSpans()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]) { Fallback = null, DropCap = new DropCap(3) };
        layout.ProcessText("In olden times when wishing still helped one, there lived a king whose daughters were all beautiful",
            20, new Size<double>(150, double.NaN), TextWrapping.WrapByWords, TextTrimming.None, HorizontalTextAlignment.Left,
            VerticalTextAlignment.Top);
        var top = layout.GetLine(1).Top;
        var bottom = layout.GetLine(2).Top + layout.GetLine(2).Height;
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;

        var run = layout.BuildGlyphRun(device, new RectangleF(-1000, (float)top, 4000, (float)(bottom - top)));

        Assert.That(run.Glyphs.Take(run.Count).Select(g => (int)g.Place.X), Does.Contain(0));
    }
}
