using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// Glyphs a document has placed itself - a PDF page - are drawn where it put them: nothing shaped, nothing snapped.
[TestFixture]
public class PlacedGlyphTests
{
    private const double Size = 12;

    [TearDown]
    public void ReleaseDevices()
    {
        FontAtlasStore.SynchronousFill = false;
        FontAtlasStore.Reset();
        GpuFixture.ReleaseRenderDevices();
    }

    private static Typeface Font() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));

    private static uint IndexOf(IFont font, char symbol) => font.GetGlyphByCharacter(symbol).Index;

    [Test]
    public void EachGlyph_LiesAtItsPenPosition_OnAnUnroundedBaseline()
    {
        var typeface = Font();
        var font = typeface.Fonts[0];
        var layout = new TextLayout(typeface, font);
        var h = IndexOf(font, 'H');

        layout.ProcessGlyphs([new PositionedGlyph(h, 3.25, 10.37), new PositionedGlyph(h, 20.5, -4.6)], Size);

        var glyphs = layout.GetTextData();
        var scale = Size / font.UnitsPerEm;
        var glyph = font.GetGlyphByIndex(h);
        var top = glyph.BoundingRectangle.Height + glyph.BoundingRectangle.Y;
        Assert.Multiple(() =>
        {
            Assert.That(glyphs, Has.Length.EqualTo(2));
            Assert.That(glyphs[0].Rect.X, Is.EqualTo(3.25 + font.GetLeftSideBearing(h) * scale).Within(1e-4));
            Assert.That(glyphs[0].Rect.Y, Is.EqualTo(10.37 - top * scale).Within(1e-4), "not snapped to 10");
            Assert.That(glyphs[0].Rect.Bottom, Is.EqualTo(10.37 - glyph.BoundingRectangle.Y * scale).Within(1e-4));
            Assert.That(glyphs[1].Rect.Y, Is.EqualTo(-4.6 - top * scale).Within(1e-4), "above the origin too");
            Assert.That(glyphs.All(g => g.Placed && g.Font == font && g.FontSize == Size));
            Assert.That(layout.Text, Has.Length.EqualTo(2));
            Assert.That(layout.CalculatedLayoutSize.Width, Is.EqualTo(glyphs[1].Rect.Right).Within(1e-4));
        });
    }

    [Test]
    public void PlacingAgain_ReplacesTheGlyphs_AndMovesTheRevisionOn()
    {
        var typeface = Font();
        var font = typeface.Fonts[0];
        var layout = new TextLayout(typeface, font);
        layout.ProcessText("words laid out", Size, new Mathematics.Size<double>(double.NaN, double.NaN),
            TextWrapping.NoWrap, TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var revision = layout.Revision;

        layout.ProcessGlyphs([new PositionedGlyph(IndexOf(font, 'A'), 0, 0)], Size);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Revision, Is.GreaterThan(revision));
            Assert.That(layout.GetTextData(), Has.Length.EqualTo(1));
            Assert.That(layout.GetTextData()[0].Glyph.Index, Is.EqualTo(IndexOf(font, 'A')));
        });
    }

    [Test]
    public void AGlyphAtNoPoint_IsLeftOut_AndTheSizeStaysANumber()
    {
        var typeface = Font();
        var font = typeface.Fonts[0];
        var layout = new TextLayout(typeface, font);
        var h = IndexOf(font, 'H');

        var size = layout.ProcessGlyphs([new PositionedGlyph(h, double.NaN, 0), new PositionedGlyph(h, 5, 20)], Size);

        Assert.Multiple(() =>
        {
            Assert.That(layout.GetTextData(), Has.Length.EqualTo(1));
            Assert.That(double.IsFinite(size.Width) && double.IsFinite(size.Height));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => layout.ProcessGlyphs([], 0));
        });
    }

    [Test]
    public void TextLaidOutAfterPlacedGlyphs_IsLaidOutAfresh()
    {
        var typeface = Font();
        var font = typeface.Fonts[0];
        var layout = new TextLayout(typeface, font);
        layout.ProcessGlyphs([new PositionedGlyph(IndexOf(font, 'H'), 0, 0)], Size);

        layout.ProcessText("�", Size, new Mathematics.Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);

        Assert.That(layout.GetTextData().All(g => !g.Placed), "the string's own glyph, not the placed one");
    }

    [Test]
    public void TheQuads_KeepTheFractionOfTheBaseline()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var typeface = Font();
        var font = typeface.Fonts[0];
        var h = IndexOf(font, 'H');
        var whole = new TextLayout(typeface, font);
        var shifted = new TextLayout(typeface, font);
        whole.ProcessGlyphs([new PositionedGlyph(h, 5, 20)], Size);
        shifted.ProcessGlyphs([new PositionedGlyph(h, 5.3, 20.37)], Size);

        whole.Update(device);
        shifted.Update(device);
        var a = whole.SnapshotGlyphs();
        var b = shifted.SnapshotGlyphs();

        Assert.Multiple(() =>
        {
            Assert.That(a.Count, Is.EqualTo(1), "one quad for the one glyph");
            Assert.That(b.Count, Is.EqualTo(1));
            Assert.That(b.Glyphs[0].ArrangeRect.X - a.Glyphs[0].ArrangeRect.X, Is.EqualTo(0.3).Within(1e-4));
            Assert.That(b.Glyphs[0].ArrangeRect.Y - a.Glyphs[0].ArrangeRect.Y, Is.EqualTo(0.37).Within(1e-4));
            Assert.That(b.Glyphs[0].ArrangeRect.W, Is.EqualTo(a.Glyphs[0].ArrangeRect.W).Within(1e-5));
        });
    }
}
