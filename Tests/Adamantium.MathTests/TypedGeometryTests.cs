using System;
using Adamantium.Mathematics;
using Adamantium.Mathematics.Typed;
using NUnit.Framework;
using Rectangle = Adamantium.Mathematics.Typed.Rectangle;
using Size = Adamantium.Mathematics.Typed.Size;
using Vector2 = Adamantium.Mathematics.Typed.Vector2;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedGeometryTests
{
    [Test]
    public void Offset_MovesWithoutGrowing()
    {
        var box = Rectangle.Create(10, 20, 100, 50);

        box.Offset(5, -3);

        Assert.That(box, Is.EqualTo(Rectangle.Create(15, 17, 100, 50)));
    }

    [Test]
    public void SettingLeftAndTop_MovesTheBox_AsTheAtlasPacksGlyphs()
    {
        var glyph = Rectangle.Create(0, 0, 30, 40);

        glyph.Left = 200;
        glyph.Top = 120;

        Assert.That(glyph, Is.EqualTo(Rectangle.Create(200, 120, 30, 40)));
    }

    [Test]
    public void SettingEdgesInOrder_GivesTheBoxBetweenThem_AsTextureCoordinatesAreWritten()
    {
        var uv = Rectangle<float>.Empty;

        uv.Left = 0.25f;
        uv.Top = 0.5f;
        uv.Right = 0.75f;
        uv.Bottom = 0.625f;

        Assert.That((uv.X, uv.Y, uv.Right, uv.Bottom), Is.EqualTo((0.25f, 0.5f, 0.75f, 0.625f)));
    }

    [Test]
    public void Contains_CountsTheEdges()
    {
        var box = Rectangle.Create(0f, 0f, 10f, 5f);

        Assert.That(box.Contains(10f, 5f), Is.True);
        Assert.That(box.Contains(Vector2.Create(0f, 0f)), Is.True);
        Assert.That(box.Contains(10.001f, 2f), Is.False);
        Assert.That(box.Contains(box), Is.True);
        Assert.That(box.Contains(Rectangle.Create(1f, 1f, 9.5f, 1f)), Is.False);
    }

    [Test]
    public void WholeNumberContains_TakesPointsAsCells_AsIntersectsDoes()
    {
        var pixels = Rectangle.Create(0, 0, 10, 10);

        Assert.That(pixels.Contains(0, 0) && pixels.Contains(9, 9), Is.True);
        Assert.That(pixels.Contains(10, 10) || pixels.Contains(10, 0) || pixels.Contains(0, 10), Is.False);
        Assert.That(pixels.Intersects(Rectangle.Create(10, 10, 1, 1)), Is.False);
        Assert.That(pixels.Contains(pixels), Is.True);
    }

    [Test]
    public void Intersect_OfBoxesThatOnlyTouch_IsEmpty_AndTheyDoNotIntersect()
    {
        var left = Rectangle.Create(0f, 0f, 10f, 10f);
        var right = Rectangle.Create(10f, 0f, 10f, 10f);

        Assert.That(Rectangle<float>.Intersect(left, right), Is.EqualTo(Rectangle<float>.Empty));
        Assert.That(left.Intersects(right), Is.False);
        Assert.That(Rectangle<float>.Intersect(left, Rectangle.Create(5f, 5f, 10f, 10f)), Is.EqualTo(Rectangle.Create(5f, 5f, 5f, 5f)));
    }

    [Test]
    public void Center_OfAnIntegerBox_HalvesInWholeNumbers()
    {
        Assert.That(Rectangle.Create(0, 0, 5, 7).Center, Is.EqualTo(Vector2.Create(2, 3)));
        Assert.That(Rectangle.Create(0f, 0f, 5f, 7f).Center, Is.EqualTo(Vector2.Create(2.5f, 3.5f)));
    }

    [Test]
    public void IsEmpty_MeansNoArea()
    {
        Assert.That(Rectangle<int>.Empty.IsEmpty, Is.True);
        Assert.That(Rectangle.Create(5, 5, 0, 10).IsEmpty, Is.True);
        Assert.That(Rectangle.Create(0, 0, 1, 1).IsEmpty, Is.False);
    }

    [Test]
    public void CreateBounding_HoldsEveryPoint()
    {
        var box = Rectangle.CreateBounding<double>([Vector2.Create(3.0, -1.0), Vector2.Create(-2.0, 4.0), Vector2.Create(1.0, 0.5)]);

        Assert.That(box, Is.EqualTo(Rectangle.Create(-2.0, -1.0, 5.0, 5.0)));
        Assert.Throws<ArgumentException>(() => Rectangle.CreateBounding<double>([]));
    }

    [Test]
    public void Size_DeflatesToNoLessThanZero_AndConstrains()
    {
        var size = Size.Create(100.0, 40.0);

        Assert.That(size.Deflate(new Thickness(10, 30, 10, 30)), Is.EqualTo(Size.Create(80.0, 0.0)));
        Assert.That(size.Inflate(new Thickness(1, 2, 3, 4)), Is.EqualTo(Size.Create(104.0, 46.0)));
        Assert.That(size.Constrain(Size.Create(50.0, 1000.0)), Is.EqualTo(Size.Create(50.0, 40.0)));
        Assert.That(Size<double>.Infinity.Width, Is.EqualTo(double.PositiveInfinity));
    }

    [Test]
    public void Ray2_ReachesItsPoints_ComparesExactly_AndConvertsAsACast()
    {
        var ray = Ray2.Create(Vector2.Create(1.0, 2.0), Vector2.Create(3.0, -4.0));
        var nudged = Ray2.Create(Vector2.Create(1.0, 2.0 + 1e-14), Vector2.Create(3.0, -4.0));
        var nan = Ray2.Create(Vector2.Create(double.NaN, 0.0), Vector2.Create(1.0, 0.0));

        Assert.That(ray.PointAt(0.5), Is.EqualTo(Vector2.Create(2.5, 0.0)));
        Assert.That(ray == nudged, Is.False);
        Assert.That(Ray2<double>.NearEqual(ray, nudged), Is.True);
        var same = nan;
        Assert.That(nan == same, Is.False);
        Assert.That(nan.Equals(same), Is.True);
        Assert.That(ray.As<float>(), Is.EqualTo(Ray2.Create(Vector2.Create(1f, 2f), Vector2.Create(3f, -4f))));
    }
}
