using System;
using Adamantium.Mathematics;
using NUnit.Framework;
using Matrix3x3D = Adamantium.Mathematics.Matrix3x3<double>;
using Vector3D = Adamantium.Mathematics.Vector3<double>;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedMatrix3x3Tests
{
    private static readonly Matrix3x3D Sample = new(2, -1, 0.5, 1, 4, -2, -3, 0.25, 1);

    [Test]
    public void Product_IsRowTimesColumn()
    {
        var other = Matrix3x3D.Transpose(Sample) + Matrix3x3D.Identity;

        var product = Sample * other;

        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                var expected = 0.0;
                for (var k = 0; k < 3; k++)
                {
                    expected += Sample[r, k] * other[k, c];
                }

                Assert.That(product[r, c], Is.EqualTo(expected).Within(1e-12), $"[{r}, {c}]");
            }
        }
    }

    [Test]
    public void Inverse_TimesTheMatrix_IsIdentity()
    {
        Assert.That(Matrix3x3D.NearEqual(Matrix3x3D.Invert(Sample) * Sample, Matrix3x3D.Identity), Is.True);
        Assert.That(Matrix3x3D.Invert(Matrix3x3D.Zero), Is.EqualTo(Matrix3x3D.Zero));
    }

    [Test]
    public void TriangularForms_AreTriangular_AndEchelonLeadsAreOne()
    {
        var upper = Matrix3x3D.UpperTriangularForm(Sample);
        var lower = Matrix3x3D.LowerTriangularForm(Sample);
        var echelon = Matrix3x3D.RowEchelonForm(Sample);

        Assert.That(new[] { upper.M21, upper.M31, upper.M32 }, Is.All.EqualTo(0).Within(1e-12));
        Assert.That(new[] { lower.M12, lower.M13, lower.M23 }, Is.All.EqualTo(0).Within(1e-12));
        Assert.That(new[] { echelon.M21, echelon.M31, echelon.M32 }, Is.All.EqualTo(0).Within(1e-12));
        Assert.That(new[] { echelon.M11, echelon.M22, echelon.M33 }, Is.All.EqualTo(1).Within(1e-12));
        Assert.That(Math.Abs(upper.Determinant()), Is.EqualTo(Math.Abs(Sample.Determinant())).Within(1e-9));
    }

    [Test]
    public void TriangularForms_OfASmallScale_AreTriangular()
    {
        var small = new Matrix3x3<float>(2e-7f, 1e-7f, 0, 4e-7f, 3e-7f, 0, 0, 0, 1e-7f);

        var upper = Matrix3x3<float>.UpperTriangularForm(small);
        var echelon = Matrix3x3<float>.RowEchelonForm(small);

        Assert.That(new[] { upper.M21, upper.M31, upper.M32 }, Is.All.EqualTo(0));
        Assert.That(new[] { echelon.M11, echelon.M22, echelon.M33 }, Is.All.EqualTo(1).Within(1e-6f));
    }

    [Test]
    public void UpperTriangularForm_OfARowScaledMatrix_IsTriangular()
    {
        var upper = Matrix3x3D.UpperTriangularForm(new Matrix3x3D(1e9, 1e9, 0, 1, 2, 0, 0, 0, 1));
        var lower = Matrix3x3D.LowerTriangularForm(Matrix3x3D.Transpose(new Matrix3x3D(1e9, 1e9, 0, 1, 2, 0, 0, 0, 1)));

        Assert.That(new[] { upper.M21, upper.M31, upper.M32 }, Is.All.EqualTo(0));
        Assert.That(new[] { lower.M12, lower.M13, lower.M23 }, Is.All.EqualTo(0));
        Assert.That(upper.Determinant(), Is.EqualTo(1e9).Within(1e-3));
    }

    [Test]
    public void Inverse_OfATinyScale_IsFinite()
    {
        var inverse = Matrix3x3<float>.Invert(Matrix3x3.CreateScale(1e-13f));

        Assert.That(inverse.M11, Is.EqualTo(1e13f).Within(1e7f));
    }

    [Test]
    public void Decompose_OfAMirror_RebuildsIt()
    {
        var mirror = Matrix3x3.CreateScale(-1.0, 1.0, 1.0);

        Assert.That(mirror.Decompose(out var scale, out var turn), Is.True);
        Assert.That(mirror.DecomposeUniformScale(out var uniform, out _), Is.True);

        Assert.That(turn.IsNormalized, Is.True);
        Assert.That(Matrix3x3D.NearEqual(Matrix3x3.CreateScale(scale) * Matrix3x3.CreateFromQuaternion(turn), mirror), Is.True);
        Assert.That(uniform, Is.EqualTo(-1.0));
    }

    [Test]
    public void Turn_IsTheSameAsTheUpperLeftOfTheFourByFour()
    {
        var turn = Quaternion.CreateFromYawPitchRoll(0.3, -0.2, 0.9);

        var small = Matrix3x3.CreateFromQuaternion(turn);
        var large = Matrix4x4.CreateFromQuaternion(turn);

        Assert.That(small, Is.EqualTo((Matrix3x3D)large));
        Assert.That((Matrix3x3D)(Matrix4x4<double>)small, Is.EqualTo(small));
        Assert.That(Quaternion<double>.NearEqual(Quaternion.CreateFromRotationMatrix(small), turn), Is.True);
    }

    [Test]
    public void Decompose_GivesBackScaleAndRotation()
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.Create(1.0, 2.0, -1.0), 0.7);
        var transform = Matrix3x3.CreateScale(2.0, 0.5, 3.0) * Matrix3x3.CreateFromQuaternion(rotation);

        Assert.That(transform.Decompose(out var scale, out var turn), Is.True);
        Assert.That(Vector3D.NearEqual(scale, Vector3.Create(2.0, 0.5, 3.0)), Is.True);
        Assert.That(Quaternion<double>.NearEqual(turn, rotation), Is.True);
    }

    [Test]
    public void Facing_TurnsForwardTowardsTheTarget()
    {
        var position = Vector3.Create(1.0, 0.0, 2.0);
        var target = Vector3.Create(4.0, 0.0, -2.0);

        var facing = Quaternion.CreateFacing(position, target, Vector3D.Up);

        var forward = Vector3D.Transform(Vector3D.ForwardRH, facing);
        Assert.That(Vector3D.NearEqual(forward, Vector3D.Normalize(target - position)), Is.True);
        Assert.That(Vector3D.NearEqual(Vector3D.Transform(Vector3D.Up, facing), Vector3D.Up), Is.True);
    }

    [Test]
    public void LookTo_IsLookAtFromTheOrigin()
    {
        var direction = Vector3.Create(0.3, -0.5, 2.0);

        Assert.That(Quaternion.CreateLookToLH(direction, Vector3D.Up),
            Is.EqualTo(Quaternion.CreateLookAtLH(Vector3D.Zero, direction, Vector3D.Up)));
        Assert.That(Quaternion.CreateLookToRH(direction, Vector3D.Up),
            Is.EqualTo(Quaternion.CreateLookAtRH(Vector3D.Zero, direction, Vector3D.Up)));
    }

    [Test]
    public void Indexers_ReadRowByRow()
    {
        var m = Sample;

        m[2, 1] = 9;

        Assert.That(m[7], Is.EqualTo(9.0));
        Assert.That(m.M32, Is.EqualTo(9.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = m[9]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = m[3, 0]);
    }

    [Test]
    public void Layout_IsNineElementsRowByRow()
    {
        Assert.That(Matrix3x3<float>.SizeInBytes, Is.EqualTo(36));
        Assert.That(Matrix3x3D.SizeInBytes, Is.EqualTo(72));
        var m = Sample;
        var elements = System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix3x3D, double>(new Span<Matrix3x3D>(ref m));
        Assert.That(elements.ToArray(), Is.EqualTo(Sample.ToArray()));
    }
}
