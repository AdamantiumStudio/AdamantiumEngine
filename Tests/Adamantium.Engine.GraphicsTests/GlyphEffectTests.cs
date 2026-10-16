using System;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

// Effects move and recolor the glyphs of a range in the snapshot the renderer draws; the layout stays as it was laid.
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

    private static TextLayout Layout()
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText(Text, 40, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static FrozenGlyphRun Run(params GlyphEffect[] effects)
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout();
        var run = layout.BuildGlyphRun(device, null, effects);
        Assume.That(run.Count, Is.EqualTo(Text.Length), "one glyph a letter");
        return run;
    }

    private static GlyphEffect Effect(int start, int length, Matrix3x2F transform, bool aroundGlyph, Vector4F? color,
        Vector2F shift = default) => new(start, length, GlyphEffect.Flat(transform), aroundGlyph, color, shift);

    // Where the shader puts a point of the glyph.
    private static Vector2F Moved(FontItem item, Vector2F p)
    {
        var w = 1 + item.EffectPerspective.Z + p.X * item.EffectPerspective.X + p.Y * item.EffectPerspective.Y;
        return new Vector2F(
            (p.X + p.X * item.Effect.X + p.Y * item.Effect.Z + item.EffectOffset.X) / w,
            (p.Y + p.X * item.Effect.Y + p.Y * item.Effect.W + item.EffectOffset.Y) / w);
    }

    private static Vector2F CenterOf(TextLayout layout, int character)
    {
        var rect = layout.GetTextData().First(g => g.PositionInString == character).Rect;
        return new Vector2F(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    [Test]
    public void AnEffect_MovesTheGlyphsOfItsRange_AndNoOthers()
    {
        var run = Run(Effect(1, 1, Matrix3x2F.Rotation(0.5f), true, null));

        Assert.Multiple(() =>
        {
            Assert.That(run.Glyphs[0].Effect, Is.EqualTo(default(Vector4F)));
            Assert.That(run.Glyphs[0].EffectOffset, Is.EqualTo(default(Vector2F)));
            Assert.That(run.Glyphs[1].Effect, Is.Not.EqualTo(default(Vector4F)));
            Assert.That(run.Glyphs[2].Effect, Is.EqualTo(default(Vector4F)));
        });
    }

    [Test]
    public void AroundTheGlyph_ItsCenterStays_WhileItTurns()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout();
        var run = layout.BuildGlyphRun(device, null, [Effect(1, 1, Matrix3x2F.Rotation(1f), true, null)]);
        var center = CenterOf(layout, 1);
        var corner = center + new Vector2F(5, 0);

        var moved = Moved(run.Glyphs[1], center);
        var turned = Moved(run.Glyphs[1], corner);

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
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout();
        var run = layout.BuildGlyphRun(device, null, [Effect(0, 3, Matrix3x2F.Scaling(2f), false, null)]);
        var center = CenterOf(layout, 2);

        var moved = Moved(run.Glyphs[2], center);

        Assert.That(moved.X, Is.EqualTo(center.X * 2).Within(1e-3));
        Assert.That(moved.Y, Is.EqualTo(center.Y * 2).Within(1e-3));
    }

    [Test]
    public void AShift_MovesEachGlyphByItsOwnSize()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout();
        var run = layout.BuildGlyphRun(device, null,
            [Effect(0, 3, Matrix3x2F.Identity, true, null, new Vector2F(0, -1))]);
        var heights = layout.GetTextData().Where(g => g.PositionInString >= 0).Select(g => g.Rect.Height).ToArray();

        Assert.Multiple(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.That(run.Glyphs[i].EffectOffset.Y, Is.EqualTo(-heights[i]).Within(1e-3));
            }
        });
    }

    [Test]
    public void APerspective_NarrowsTheFarSide_AboutTheGlyphsCenter()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout();
        var perspective = new Matrix3x3F(1, 0, 0.01f, 0, 1, 0, 0, 0, 1);
        var run = layout.BuildGlyphRun(device, null, [new GlyphEffect(1, 1, perspective, true, null)]);
        var center = CenterOf(layout, 1);
        var nearTop = Moved(run.Glyphs[1], center + new Vector2F(-10, -10));
        var nearBottom = Moved(run.Glyphs[1], center + new Vector2F(-10, 10));
        var farTop = Moved(run.Glyphs[1], center + new Vector2F(10, -10));
        var farBottom = Moved(run.Glyphs[1], center + new Vector2F(10, 10));
        var stays = Moved(run.Glyphs[1], center);

        Assert.Multiple(() =>
        {
            Assert.That(run.Glyphs[1].EffectPerspective, Is.Not.EqualTo(default(Vector4F)));
            Assert.That(stays.X, Is.EqualTo(center.X).Within(1e-3), "the center stays");
            Assert.That(stays.Y, Is.EqualTo(center.Y).Within(1e-3));
            Assert.That(farBottom.Y - farTop.Y, Is.LessThan(nearBottom.Y - nearTop.Y), "the far side is shorter");
            Assert.That(run.Glyphs[0].EffectPerspective, Is.EqualTo(default(Vector4F)), "the others stay flat");
        });
    }

    [Test]
    public void APerspective_FarFromTheLayoutsOrigin_KeepsTheGlyphInFront()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        const string text = "abcdefghijklmnopqrstuvwxyz";
        layout.ProcessText(text, 40, new Size(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var last = text.Length - 1;
        var perspective = new Matrix3x3F(1, 0, 0.01f, 0, 1, 0, 0, 0, 1);
        var run = layout.BuildGlyphRun(device, null, [new GlyphEffect(last, 1, perspective, true, null)]);
        var center = CenterOf(layout, last);
        var glyph = run.Glyphs[run.Count - 1];
        var w = 1 + glyph.EffectPerspective.Z + center.X * glyph.EffectPerspective.X + center.Y * glyph.EffectPerspective.Y;

        var stays = Moved(glyph, center);

        Assume.That(center.X * 0.01f, Is.GreaterThan(1), "far enough that w at the origin is below zero");
        Assert.Multiple(() =>
        {
            Assert.That(w, Is.GreaterThan(0), "in front of the viewer, not clipped");
            Assert.That(stays.X, Is.EqualTo(center.X).Within(1e-2));
            Assert.That(stays.Y, Is.EqualTo(center.Y).Within(1e-2));
        });
    }

    [Test]
    public void ScaledToNothing_IsAnEffect_NotNone()
    {
        var run = Run(Effect(0, 1, Matrix3x2F.Scaling(0f), true, null));

        Assert.That(run.Glyphs[0].Effect, Is.EqualTo(new Vector4F(-1, 0, 0, -1)));
    }

    [Test]
    public void EffectsOverOneGlyph_ApplyInTheirOrder()
    {
        var run = Run(
            Effect(0, 3, Matrix3x2F.Translation(10, 0), false, null),
            Effect(0, 3, Matrix3x2F.Scaling(2f), false, null));

        var moved = Moved(run.Glyphs[0], new Vector2F(0, 0));

        Assert.That(moved.X, Is.EqualTo(20).Within(1e-3), "moved first, then scaled");
    }

    [Test]
    public void AColor_RecolorsTheRange_AndTheRunHasRoomForWhatMoved()
    {
        var red = new Vector4F(1, 0, 0, 1);
        var plain = Run();
        var run = Run(Effect(2, 1, Matrix3x2F.Translation(0, -30), true, red));

        Assert.Multiple(() =>
        {
            Assert.That(run.Glyphs[2].Color, Is.EqualTo(red));
            Assert.That(run.Glyphs[1].Color, Is.EqualTo(plain.Glyphs[1].Color));
            Assert.That(run.EffectReach, Is.EqualTo(30).Within(1e-3));
            Assert.That(run.EffectPadding, Is.EqualTo(plain.EffectPadding + 30).Within(1));
        });
    }

    [Test]
    public void AnEllipsisTheLayoutAdds_MovesWithTheCharacterBeforeIt()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Regular.ttf"));
        var layout = new TextLayout(typeface, typeface.Fonts[0]);
        layout.ProcessText("abcdefghijklmnop", 40, new Size(120, double.NaN), TextWrapping.NoWrap,
            TextTrimming.CharEllipses, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        Assume.That(layout.GetTextData().Any(g => g.PositionInString < 0), "the layout added glyphs of its own");

        var run = layout.BuildGlyphRun(device, null, [Effect(0, 16, Matrix3x2F.Translation(0, -30), true, null)]);

        Assert.That(run.Glyphs.Take(run.Count).Select(g => g.EffectOffset.Y), Is.All.EqualTo(-30).Within(1e-3));
    }

    [Test]
    public void TheLayout_StaysAsItWasLaid()
    {
        var device = GpuFixture.CreateRenderDevice();
        FontAtlasStore.SynchronousFill = true;
        var layout = Layout();
        layout.BuildGlyphRun(device, null, [Effect(0, 3, Matrix3x2F.Rotation(1f), true, null)]);

        var after = layout.SnapshotGlyphs();

        Assert.That(after.Glyphs.Take(after.Count).All(g => g.Effect == default && g.EffectOffset == default), Is.True);
    }
}
