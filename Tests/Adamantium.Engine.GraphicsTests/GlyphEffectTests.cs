using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// Effects move and recolor the glyphs of a range where the font shaders draw them; the snapshot only says which
// character each glyph draws and in which cell, and the layout stays as it was laid.
[TestFixture]
public class GlyphEffectTests
{
    private const string Text = "abc";

    [TearDown]
    public void ReleaseDevices()
    {
        FontAtlasStore.SynchronousFill = false;
        FontAtlasStore.Reset();
        GpuFixture.ReleaseRenderDevices();
    }

    private static TextLayout Layout(string text = Text, double width = double.NaN,
        TextTrimming trimming = TextTrimming.None)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText(text, 40, new Size(width, double.NaN), TextWrapping.NoWrap, trimming,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static FrozenGlyphRun Run(TextLayout layout)
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        return layout.BuildGlyphRun(device, null);
    }

    private static GlyphEffect Effect(int start, int length, Matrix3x2F transform, bool aroundGlyph, Vector4F? color = null,
        Vector2F shift = default) => new(start, length, GlyphEffect.Flat(transform), aroundGlyph, color, shift);

    // Where the shaders put a point of the glyph.
    private static Vector2F Moved(FontItem glyph, Vector2F p, params GlyphEffect[] effects)
    {
        GlyphEffect.Compose(effects, glyph.Place.X, glyph.Cell, out var m);
        var w = p.X * m.M13 + p.Y * m.M23 + m.M33;
        return new Vector2F((p.X * m.M11 + p.Y * m.M21 + m.M31) / w, (p.X * m.M12 + p.Y * m.M22 + m.M32) / w);
    }

    private static Vector2F CenterOf(TextLayout layout, int character)
    {
        var rect = layout.GetTextData().First(g => g.PositionInString == character).Rect;
        return new Vector2F(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    [Test]
    public void EachGlyph_KnowsTheCharacterItDraws_AndItsCell()
    {
        var layout = Layout();
        var run = Run(layout);
        var laid = layout.GetTextData().Where(g => g.PositionInString >= 0).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(run.Count, Is.EqualTo(Text.Length), "one glyph a letter");
            for (var i = 0; i < run.Count; i++)
            {
                Assert.That(run.Glyphs[i].Place, Is.EqualTo(new Vector2F(i, 1)), "drawn in the text's color");
                Assert.That(run.Glyphs[i].Cell,
                    Is.EqualTo(new Vector4F(laid[i].Rect.X, laid[i].Rect.Y, laid[i].Rect.Width, laid[i].Rect.Height)));
            }
        });
    }

    [Test]
    public void AnEffect_ReachesTheGlyphsOfItsRange_AndNoOthers()
    {
        var run = Run(Layout());
        GlyphEffect[] effects = [Effect(1, 1, Matrix3x2F.Rotation(0.5f), true)];

        Assert.Multiple(() =>
        {
            Assert.That(GlyphEffect.Compose(effects, run.Glyphs[0].Place.X, run.Glyphs[0].Cell, out _), Is.False);
            Assert.That(GlyphEffect.Compose(effects, run.Glyphs[1].Place.X, run.Glyphs[1].Cell, out _), Is.True);
            Assert.That(GlyphEffect.Compose(effects, run.Glyphs[2].Place.X, run.Glyphs[2].Cell, out _), Is.False);
        });
    }

    [Test]
    public void AroundTheGlyph_ItsCenterStays_WhileItTurns()
    {
        var layout = Layout();
        var run = Run(layout);
        var turn = Effect(1, 1, Matrix3x2F.Rotation(1f), true);
        var center = CenterOf(layout, 1);
        var corner = center + new Vector2F(5, 0);

        var moved = Moved(run.Glyphs[1], center, turn);
        var turned = Moved(run.Glyphs[1], corner, turn);

        Assert.Multiple(() =>
        {
            Assert.That(moved.X, Is.EqualTo(center.X).Within(1e-3));
            Assert.That(moved.Y, Is.EqualTo(center.Y).Within(1e-3));
            Assert.That(turned.X, Is.EqualTo(center.X + 5 * MathF.Cos(1f)).Within(1e-3));
            Assert.That(turned.Y, Is.EqualTo(center.Y + 5 * MathF.Sin(1f)).Within(1e-3));
        });
    }

    [Test]
    public void AboutTheOrigin_TheTransformIsTheLayouts()
    {
        var layout = Layout();
        var run = Run(layout);
        var center = CenterOf(layout, 2);

        var moved = Moved(run.Glyphs[2], center, Effect(0, 3, Matrix3x2F.Scaling(2f), false));

        Assert.That(moved.X, Is.EqualTo(center.X * 2).Within(1e-3));
        Assert.That(moved.Y, Is.EqualTo(center.Y * 2).Within(1e-3));
    }

    [Test]
    public void AShift_MovesEachGlyphByItsOwnSize()
    {
        var layout = Layout();
        var run = Run(layout);
        var raise = Effect(0, 3, Matrix3x2F.Identity, true, shift: new Vector2F(0, -1));
        var heights = layout.GetTextData().Where(g => g.PositionInString >= 0).Select(g => g.Rect.Height).ToArray();

        Assert.Multiple(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                var center = CenterOf(layout, i);
                Assert.That(Moved(run.Glyphs[i], center, raise).Y, Is.EqualTo(center.Y - heights[i]).Within(1e-3));
            }
        });
    }

    [Test]
    public void APerspective_NarrowsTheFarSide_AboutTheGlyphsCenter()
    {
        var layout = Layout();
        var run = Run(layout);
        var tip = new GlyphEffect(1, 1, new Matrix3x3F(1, 0, 0.01f, 0, 1, 0, 0, 0, 1), true, null);
        var center = CenterOf(layout, 1);
        var nearTop = Moved(run.Glyphs[1], center + new Vector2F(-10, -10), tip);
        var nearBottom = Moved(run.Glyphs[1], center + new Vector2F(-10, 10), tip);
        var farTop = Moved(run.Glyphs[1], center + new Vector2F(10, -10), tip);
        var farBottom = Moved(run.Glyphs[1], center + new Vector2F(10, 10), tip);
        var stays = Moved(run.Glyphs[1], center, tip);

        Assert.Multiple(() =>
        {
            Assert.That(stays.X, Is.EqualTo(center.X).Within(1e-3), "the center stays");
            Assert.That(stays.Y, Is.EqualTo(center.Y).Within(1e-3));
            Assert.That(farBottom.Y - farTop.Y, Is.LessThan(nearBottom.Y - nearTop.Y), "the far side is shorter");
        });
    }

    [Test]
    public void APerspective_FarFromTheLayoutsOrigin_KeepsTheGlyphInFront()
    {
        const string text = "abcdefghijklmnopqrstuvwxyz";
        var layout = Layout(text);
        var run = Run(layout);
        var last = text.Length - 1;
        var tip = new GlyphEffect(last, 1, new Matrix3x3F(1, 0, 0.01f, 0, 1, 0, 0, 0, 1), true, null);
        var center = CenterOf(layout, last);
        var glyph = run.Glyphs[run.Count - 1];
        GlyphEffect.Compose([tip], glyph.Place.X, glyph.Cell, out var m);
        var w = center.X * m.M13 + center.Y * m.M23 + m.M33;

        var stays = Moved(glyph, center, tip);

        Assume.That(center.X * 0.01f, Is.GreaterThan(1), "far enough that w at the origin is below zero");
        Assert.Multiple(() =>
        {
            Assert.That(w, Is.GreaterThan(0), "in front of the viewer, not clipped");
            Assert.That(stays.X, Is.EqualTo(center.X).Within(1e-2));
            Assert.That(stays.Y, Is.EqualTo(center.Y).Within(1e-2));
        });
    }

    [Test]
    public void EffectsOverOneGlyph_ApplyInTheirOrder()
    {
        var run = Run(Layout());

        var moved = Moved(run.Glyphs[0], new Vector2F(0, 0),
            Effect(0, 3, Matrix3x2F.Translation(10, 0), false), Effect(0, 3, Matrix3x2F.Scaling(2f), false));

        Assert.That(moved.X, Is.EqualTo(20).Within(1e-3), "moved first, then scaled");
    }

    [Test]
    public void TheRun_HasRoomForWhatAnEffectMoves()
    {
        var run = Run(Layout());
        GlyphEffect[] lift = [Effect(2, 1, Matrix3x2F.Translation(0, -30), true)];

        Assert.Multiple(() =>
        {
            Assert.That(run.ReachOf([]), Is.Zero);
            Assert.That(run.ReachOf(lift), Is.EqualTo(30).Within(1e-3));
            Assert.That(run.PaddingFor(lift), Is.EqualTo(run.PaddingFor([]) + 30).Within(1));
        });
    }

    [Test]
    public void AGlyphTippedBehindTheViewer_ReachesAsFarAsCanBeSaid()
    {
        var run = Run(Layout());
        GlyphEffect[] behind = [new(0, 1, new Matrix3x3F(1, 0, 0, 0, 1, 0, 0, 0, -1), true, null)];

        Assert.That(run.ReachOf(behind), Is.EqualTo(FrozenGlyphRun.MaxReach));
    }

    [Test]
    public void AnEllipsisTheLayoutAdds_MovesWithTheCharacterBeforeIt()
    {
        var layout = Layout("abcdefghijklmnop", 120, TextTrimming.CharEllipses);
        Assume.That(layout.GetTextData().Any(g => g.PositionInString < 0), "the layout added glyphs of its own");

        var run = Run(layout);

        Assert.That(run.Glyphs.Take(run.Count).Select(g => g.Place.X), Is.All.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void ASlot_HoldsTheEffect_TheWayTheShadersReadIt()
    {
        var red = new Vector4F(1, 0, 0, 1);
        var effect = new GlyphEffect(2, 3, new Matrix3x3F(1, 2, 3, 4, 5, 6, 7, 8, 9), true, red, new Vector2F(0.5f, -1));

        effect.ToSlot(7, 9, out var rows, out var color);
        new GlyphEffect(0, 1, Matrix3x3F.Identity, false, null).ToSlot(-1, -1, out var plain, out var none);

        Assert.Multiple(() =>
        {
            Assert.That(rows, Is.EqualTo(new Matrix4x4F(1, 2, 3, 2, 4, 5, 6, 5, 7, 8, 9, 7, 0.5f, -1, 3, 9)));
            Assert.That(color, Is.EqualTo(red));
            Assert.That(plain.M34, Is.EqualTo(-1), "the last of its chain");
            Assert.That(plain.M43, Is.Zero, "about the layout's origin, no color of its own");
            Assert.That(plain.M44, Is.EqualTo(-1), "no clip");
            Assert.That(none, Is.EqualTo(default(Vector4F)));
        });
    }

    [Test]
    public void AClipsSlot_HoldsItsShape_AndWhereItIsTakenFrom()
    {
        var clip = new GlyphClip(new RectangleF(1, 2, 30, 40), new Vector4F(3, 4, 5, 6),
            Matrix3x2F.Translation(-7, -8), AfterTransform: true);

        clip.ToSlot(out var rows, out _);

        Assert.That(rows, Is.EqualTo(new Matrix4x4F(1, 2, 30, 40, 3, 4, 5, 6, 1, 0, 0, 1, -7, -8, 1, 0)));
    }
}
