using System;
using Adamantium.Mathematics;
using NUnit.Framework;
using Matrix3x2D = Adamantium.Mathematics.Matrix3x2<double>;
using Vector2D = Adamantium.Mathematics.Vector2<double>;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedMatrix3x2Tests
{
    private static readonly Matrix3x2D Sample = new(2, -1, 0.5, 3, 4, -2);

    [Test]
    public void Product_AppliesTheLeftFirst()
    {
        var point = Vector2.Create(1.5, -0.5);
        var other = Matrix3x2.CreateRotation(0.4, Vector2.Create(1.0, 2.0));

        var once = Vector2D.Transform(point, Sample * other);
        var twice = Vector2D.Transform(Vector2D.Transform(point, Sample), other);

        Assert.That(Vector2D.NearEqual(once, twice), Is.True);
    }

    [Test]
    public void Inverse_UndoesTheTransform()
    {
        var point = Vector2.Create(-3.0, 7.0);

        var back = Vector2D.Transform(Vector2D.Transform(point, Sample), Matrix3x2D.Invert(Sample));

        Assert.That(Vector2D.NearEqual(back, point), Is.True);
        Assert.That(Matrix3x2D.Invert(Matrix3x2.CreateScale(0.0, 1.0)), Is.EqualTo(Matrix3x2D.Identity));
        Assert.That(Matrix3x2<float>.Invert(Matrix3x2.CreateScale(0.0005f)).M11, Is.EqualTo(2000f).Within(1e-2f));
        foreach (var (x, y) in new[] { (1e20f, 1e-20f), (1e30f, 1e-30f) })
        {
            var inverse = Matrix3x2<float>.Invert(Matrix3x2.CreateScale(x, y));
            Assert.That(inverse.M11, Is.EqualTo(1 / x).Within(1e-6f / x), $"{x}");
            Assert.That(inverse.M22, Is.EqualTo(1 / y).Within(1e-6f / y), $"{y}");
        }

        var halfInverse = Matrix3x2<Half>.Invert(Matrix3x2.CreateScale((Half)1000, (Half)0.001));
        Assert.That((float)halfInverse.M11, Is.EqualTo(0.001f).Within(1e-5f));
        Assert.That((float)halfInverse.M22, Is.EqualTo(1000f).Within(2f));
        foreach (var scale in new[] { 1e-20f, 1e-23f, 1e20f })
        {
            var inverse = Matrix3x2<float>.Invert(Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(scale, -scale));
            Assert.That(inverse.M11, Is.EqualTo(1 / scale).Within(1e-6f / scale), $"{scale}");
            Assert.That(inverse.M12, Is.EqualTo(0f), $"{scale}");
            Assert.That(Vector2<float>.NearEqual(inverse.TranslationVector, Vector2.Create(-1f, 1f)), Is.True, $"{scale}");
        }
    }

    [Test]
    public void Inverse_OfATurnedLopsidedScale_UndoesIt()
    {
        var single = Matrix3x2.CreateRotation(0.5f) * Matrix3x2.CreateScale(1e20f, 1e-20f);
        var half = (Matrix3x2.CreateRotation(0.5) * Matrix3x2.CreateScale(1000.0, 0.001)).As<Half>();
        var tiny = new Matrix3x2<float>(MathF.Pow(2, 65), MathF.Pow(2, -86), MathF.Pow(2, 65), MathF.Pow(2, -85), 3, 4);

        Assert.That(IsNearEachRow(Matrix3x2<float>.Invert(single).As<double>(), Matrix3x2D.Invert(single.As<double>()), 1e-6), Is.True);
        Assert.That(IsNearEachRow(Matrix3x2<Half>.Invert(half).As<double>(), Matrix3x2D.Invert(half.As<double>()), 4e-3), Is.True);
        Assert.That(IsNearEachRow(Matrix3x2<float>.Invert(tiny).As<double>(), Matrix3x2D.Invert(tiny.As<double>()), 1e-6), Is.True);
    }

    [Test]
    public void RotationAroundACenter_LeavesTheCenterInPlace()
    {
        var center = Vector2.Create(3.0, -1.0);

        var turned = Vector2D.Transform(center, Matrix3x2.CreateRotation(1.1, center));

        Assert.That(Vector2D.NearEqual(turned, center), Is.True);
        Assert.That(Vector2D.NearEqual(Vector2D.Transform(center, Matrix3x2.CreateScale(2.0, 3.0, center)), center), Is.True);
    }

    [Test]
    public void Skew_MovesXAsYGrows_ByTheXAngle()
    {
        var skew = Matrix3x2.CreateSkew(Math.PI / 4, 0.0);

        Assert.That(Vector2D.NearEqual(Vector2D.Transform(Vector2.Create(0.0, 2.0), skew), Vector2.Create(2.0, 2.0)), Is.True);
        Assert.That(Vector2D.NearEqual(Vector2D.Transform(Vector2.Create(2.0, 0.0), skew), Vector2.Create(2.0, 0.0)), Is.True);
    }

    [Test]
    public void FromFourByFour_KeepsThe2DPart()
    {
        var full = Matrix4x4.CreateRotationZ(0.7) * Matrix4x4.CreateTranslation(3.0, -2.0, 5.0);

        var flat = (Matrix3x2D)full;

        Assert.That(Matrix3x2D.NearEqual(flat, Matrix3x2.CreateRotation(0.7) * Matrix3x2.CreateTranslation(3.0, -2.0)), Is.True);
    }

    [Test]
    public void Indexers_ReadRowByRow()
    {
        var m = Sample;

        m[2, 1] = 9;

        Assert.That(m[5], Is.EqualTo(9.0));
        Assert.That(m.M32, Is.EqualTo(9.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = m[6]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = m[0, 2]);
    }

    private static bool IsNearEachRow(Matrix3x2D actual, Matrix3x2D expected, double tolerance)
    {
        for (var r = 0; r < 3; r++)
        {
            var size = Math.Max(Math.Abs(expected[r, 0]), Math.Abs(expected[r, 1]));
            if (!(Math.Abs(actual[r, 0] - expected[r, 0]) <= tolerance * size) ||
                !(Math.Abs(actual[r, 1] - expected[r, 1]) <= tolerance * size))
            {
                return false;
            }
        }

        return true;
    }

    [Test]
    public void Layout_IsSixElementsRowByRow()
    {
        Assert.That(Matrix3x2<float>.SizeInBytes, Is.EqualTo(24));
        Assert.That(Matrix3x2D.SizeInBytes, Is.EqualTo(48));
        var m = Sample;
        var elements = System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix3x2D, double>(new Span<Matrix3x2D>(ref m));
        Assert.That(elements.ToArray(), Is.EqualTo(Sample.ToArray()));
    }
}
