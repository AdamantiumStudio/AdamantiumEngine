using System;
using Adamantium.Mathematics.Typed;
using NUnit.Framework;
using Matrix4x4 = Adamantium.Mathematics.Typed.Matrix4x4;
using Matrix4x4D = Adamantium.Mathematics.Typed.Matrix4x4<double>;
using Vector4D = Adamantium.Mathematics.Typed.Vector4<double>;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedMatrixTests
{
    private static readonly Matrix4x4D Sample = new(2, -1, 0.5, 3, 1, 4, -2, 0, -3, 0.25, 1, 2, 0.5, 2, -1, 5);

    [Test]
    public void Product_IsRowTimesColumn()
    {
        var other = Matrix4x4D.Transpose(Sample) + Matrix4x4D.Identity;

        var product = Sample * other;

        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                var expected = 0.0;
                for (var k = 0; k < 4; k++)
                {
                    expected += Sample[r, k] * other[k, c];
                }

                Assert.That(product[r, c], Is.EqualTo(expected).Within(1e-12), $"[{r}, {c}]");
            }
        }

        var singleProduct = (Sample.As<float>() * other.As<float>()).ToArray();
        Assert.That(singleProduct, Is.EqualTo(product.ToArray()).Within(1e-4));
    }

    [Test]
    public void Inverse_TimesTheMatrix_IsIdentity()
    {
        Assert.That(Matrix4x4D.NearEqual(Matrix4x4D.Invert(Sample) * Sample, Matrix4x4D.Identity), Is.True);
        Assert.That(Matrix4x4D.Invert(Matrix4x4D.Zero), Is.EqualTo(Matrix4x4D.Zero));
    }

    [Test]
    public void UpperTriangularForm_IsTriangularWithTheSameDeterminant()
    {
        var upper = Matrix4x4D.UpperTriangularForm(Sample);

        for (var r = 1; r < 4; r++)
        {
            for (var c = 0; c < r; c++)
            {
                Assert.That(upper[r, c], Is.EqualTo(0).Within(1e-12), $"[{r}, {c}]");
            }
        }

        Assert.That(Math.Abs(upper.Determinant()), Is.EqualTo(Math.Abs(Sample.Determinant())).Within(1e-9));
    }

    [Test]
    public void LowerTriangularForm_IsTriangular()
    {
        var lower = Matrix4x4D.LowerTriangularForm(Sample);

        for (var r = 0; r < 3; r++)
        {
            for (var c = r + 1; c < 4; c++)
            {
                Assert.That(lower[r, c], Is.EqualTo(0).Within(1e-12), $"[{r}, {c}]");
            }
        }
    }

    [Test]
    public void RowEchelonForm_HasLeadingOnesAndZerosBelow()
    {
        var echelon = Matrix4x4D.RowEchelonForm(Sample);

        for (var r = 0; r < 4; r++)
        {
            Assert.That(echelon[r, r], Is.EqualTo(1).Within(1e-12));
            for (var c = 0; c < r; c++)
            {
                Assert.That(echelon[r, c], Is.EqualTo(0).Within(1e-12), $"[{r}, {c}]");
            }
        }
    }

    [Test]
    public void ReducedRowEchelonForm_SolvesTheSystem()
    {
        var b = new Vector4D(1, -2, 3, 0.5);

        var (reduced, x) = Matrix4x4D.ReducedRowEchelonForm(Sample, b);

        Assert.That(reduced.IsIdentity, Is.True);
        for (var r = 0; r < 4; r++)
        {
            var sum = 0.0;
            for (var c = 0; c < 4; c++)
            {
                sum += Sample[r, c] * x[c];
            }

            Assert.That(sum, Is.EqualTo(b[r]).Within(1e-12));
        }
    }

    [Test]
    public void ReducedRowEchelonForm_OfZero_IsZero()
    {
        var (reduced, x) = Matrix4x4D.ReducedRowEchelonForm(Matrix4x4D.Zero, Vector4D.Zero);

        Assert.That(reduced, Is.EqualTo(Matrix4x4D.Zero));
        Assert.That(x, Is.EqualTo(Vector4D.Zero));
    }

    [Test]
    public void Decompose_GivesBackScaleRotationAndTranslation()
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(0.3, -0.2, 0.9);
        var transform = Matrix4x4.CreateScale(2.0, 3.0, 0.5) * Matrix4x4.CreateFromQuaternion(rotation) *
                        Matrix4x4.CreateTranslation(1.0, -4.0, 7.0);

        Assert.That(transform.Decompose(out var scale, out var turn, out var translation), Is.True);
        Assert.That(Vector3<double>.NearEqual(scale, Vector3.Create(2.0, 3.0, 0.5)), Is.True);
        Assert.That(Quaternion<double>.NearEqual(turn, rotation), Is.True);
        Assert.That(Vector3<double>.NearEqual(translation, Vector3.Create(1.0, -4.0, 7.0)), Is.True);
    }

    [Test]
    public void TransformCoordinate_OfAPerspective_DividesByW()
    {
        var projection = Matrix4x4.CreatePerspectiveFovLH(MathF.PI / 2, 1f, 1f, 100f);

        var near = Vector3<float>.TransformCoordinate(Vector3.Create(0f, 0f, 1f), projection);
        var far = Vector3<float>.TransformCoordinate(Vector3.Create(0f, 0f, 100f), projection);

        Assert.That(near.Z, Is.EqualTo(0f).Within(1e-6f));
        Assert.That(far.Z, Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void PerspectiveFovX_TakesRadians_AndPutsTheFieldEdgeAtTheClipEdge()
    {
        const double fov = Math.PI / 3;
        var projection = Matrix4x4.CreatePerspectiveFovX(fov, 4.0 / 3, 1.0, 100.0);
        var edge = Vector3.Create(Math.Tan(fov / 2) * 10, 0.0, 10.0);

        Assert.That(Vector3<double>.TransformCoordinate(edge, projection).X, Is.EqualTo(1).Within(1e-12));
    }

    [TestCase(0.4, 1, 0, 0, 0, 1, 0)]
    [TestCase(-0.3, 1, 2, 0.5, 0, 0, 1)]
    [TestCase(0.25, 3, -1, 2, 1, 1, 0)]
    public void Skew_TurnsTheRotationVectorByTheAngle_AndMovesAlongTheTranslationVector(double angle, double rx, double ry,
        double rz, double tx, double ty, double tz)
    {
        var rotation = Vector3.Create(rx, ry, rz);
        var translation = Vector3.Create(tx, ty, tz);
        var skew = Matrix4x4.CreateSkew(angle, rotation, translation);

        var turned = Vector3<double>.TransformNormal(rotation, skew);
        var cos = Vector3<double>.Dot(rotation, turned) / (rotation.Length() * turned.Length());
        Assert.That(Math.Acos(Math.Clamp(cos, -1, 1)), Is.EqualTo(Math.Abs(angle)).Within(1e-12));
        Assert.That(Vector3<double>.NearEqual(Vector3<double>.TransformNormal(translation, skew), translation), Is.True);
        var point = Vector3.Create(0.7, -1.3, 2.1);
        var moved = Vector3<double>.TransformNormal(point, skew) - point;
        Assert.That(Vector3<double>.Cross(moved, translation).Length(), Is.EqualTo(0).Within(1e-12));
    }

    [Test]
    public void Skew_OfParallelVectors_Throws()
    {
        Assert.Throws<ArgumentException>(() => Matrix4x4.CreateSkew(0.3, Vector3.Create(2.0, 0, 0), Vector3.Create(1.0, 0, 0)));
    }

    [Test]
    public void IsometricProjection_MirrorsXAndZ_AndLiftsY()
    {
        var projection = Matrix4x4.CreateIsometricProjection(10.0);

        var x = Vector3<double>.TransformCoordinate(Vector3<double>.UnitX, projection);
        var z = Vector3<double>.TransformCoordinate(Vector3<double>.UnitZ, projection);
        var y = Vector3<double>.TransformCoordinate(Vector3<double>.UnitY, projection);

        Assert.That(z.X, Is.EqualTo(-x.X).Within(1e-12));
        Assert.That(z.Y, Is.EqualTo(x.Y).Within(1e-12));
        Assert.That(x.Y, Is.EqualTo(x.X / 2).Within(1e-12));
        Assert.That(y.X, Is.EqualTo(0).Within(1e-12));
        Assert.That(y.Y, Is.EqualTo(2 * x.Y).Within(1e-12));
    }

    [Test]
    public void SpanTransforms_AnswerAsOneAtATime_InPlaceToo()
    {
        var projection = Matrix4x4.CreatePerspectiveFovLH(1.0, 1.5, 0.5, 50.0) * Sample;
        var turn = Quaternion.CreateFromYawPitchRoll(0.2, 0.5, -0.3);
        Span<Vector3<double>> points = [Vector3.Create(1.0, 2.0, 3.0), Vector3.Create(-4.0, 0.5, 9.0)];
        var expected = new[] { Vector3<double>.TransformCoordinate(points[0], projection),
            Vector3<double>.TransformCoordinate(points[1], projection) };
        Span<Vector4D> full = stackalloc Vector4D[2];
        Span<Vector2<double>> flat = [Vector2.Create(1.0, -1.0), Vector2.Create(0.5, 2.0)];
        var turned = new[] { Vector2<double>.Transform(flat[0], turn), Vector2<double>.Transform(flat[1], turn) };

        Vector3<double>.Transform(points, projection, full);
        Vector3<double>.TransformCoordinate(points, projection, points);
        Vector2<double>.Transform(flat, turn, flat);

        Assert.That(points.ToArray(), Is.EqualTo(expected));
        Assert.That(full[1], Is.EqualTo(Vector3<double>.Transform(Vector3.Create(-4.0, 0.5, 9.0), projection)));
        Assert.That(flat.ToArray(), Is.EqualTo(turned));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Vector4D.Transform([Vector4D.UnitX, Vector4D.UnitY], Sample, Span<Vector4D>.Empty));
    }

    [Test]
    public void Product_GivesTheBitsOfTheElementFormula_OnEveryInstructionLevel()
    {
        var other = Matrix4x4D.Invert(Sample) + Matrix4x4D.Identity * 0.1;
        var single = (Sample.As<float>(), other.As<float>());

        var product = Sample * other;
        var singleProduct = single.Item1 * single.Item2;

        for (var r = 0; r < 4; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                var expected = Sample[r, 0] * other[0, c] + Sample[r, 1] * other[1, c] +
                               (Sample[r, 2] * other[2, c] + Sample[r, 3] * other[3, c]);
                var expectedSingle = single.Item1[r, 0] * single.Item2[0, c] + single.Item1[r, 1] * single.Item2[1, c] +
                                     (single.Item1[r, 2] * single.Item2[2, c] + single.Item1[r, 3] * single.Item2[3, c]);
                Assert.That(product[r, c], Is.EqualTo(expected), $"double [{r}, {c}]");
                Assert.That(singleProduct[r, c], Is.EqualTo(expectedSingle), $"float [{r}, {c}]");
            }
        }
    }

    [Test]
    public void ReducedRowEchelonForm_PivotsOnTheLargest()
    {
        var a = new Matrix4x4D(1e-17, 1, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

        var (_, x) = Matrix4x4D.ReducedRowEchelonForm(a, new Vector4D(1, 2, 1, 1));

        Assert.That(Vector4D.NearEqual(x, new Vector4D(1, 1, 1, 1)), Is.True);
    }

    [Test]
    public void ReducedRowEchelonForm_OfATranslation_SolvesTheSystem()
    {
        var move = Matrix4x4.CreateTranslation(2e6f, 0f, 0f);
        var b = Vector4.Create(1f, 2f, 3f, 1f);

        var (reduced, x) = Matrix4x4<float>.ReducedRowEchelonForm(move, b);

        Assert.That(reduced.IsIdentity, Is.True);
        Assert.That(Vector4<float>.NearEqual(Matrix4x4Transform(move, x), b), Is.True);

        static Vector4<float> Matrix4x4Transform(Matrix4x4<float> m, Vector4<float> v) =>
            new(Vector4<float>.Dot(m.Row1, v), Vector4<float>.Dot(m.Row2, v), Vector4<float>.Dot(m.Row3, v),
                Vector4<float>.Dot(m.Row4, v));
    }

    [Test]
    public void Elimination_LeavesRowsWithNothingToTakeAlone_EvenBesideAnInfinity()
    {
        var triangular = new Matrix4x4D(2, double.PositiveInfinity, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);

        var upper = Matrix4x4D.UpperTriangularForm(triangular);
        var (_, x) = Matrix4x4D.ReducedRowEchelonForm(Matrix4x4D.Identity, new Vector4D(double.PositiveInfinity, 1, 2, 3));

        Assert.That(upper.Row2, Is.EqualTo(triangular.Row2));
        Assert.That(upper.Row4, Is.EqualTo(triangular.Row4));
        Assert.That(x, Is.EqualTo(new Vector4D(double.PositiveInfinity, 1, 2, 3)));
    }

    [Test]
    public void ReducedRowEchelonForm_CarriesANaNIntoTheSolution()
    {
        var (_, x) = Matrix4x4D.ReducedRowEchelonForm(new Matrix4x4D(double.NaN), new Vector4D(1, 2, 3, 1));

        Assert.That(double.IsNaN(x.X) || double.IsNaN(x.W), Is.True);
    }

    [Test]
    public void UpperTriangularForm_OfAFarViewInFloat_IsTriangular()
    {
        var view = Matrix4x4.CreateLookAtLH(Vector3.Create(3e6f, 10f, 4e6f), Vector3.Create(3e6f + 1, 10f, 4e6f), Vector3<float>.UnitY);

        var upper = Matrix4x4<float>.UpperTriangularForm(view);

        Assert.That(new[] { upper.M21, upper.M31, upper.M32, upper.M41, upper.M42, upper.M43 }, Is.All.EqualTo(0));
    }

    [TestCase(1e-10f)]
    [TestCase(1e10f)]
    public void Inverse_OfAnExtremeScale_IsTheReciprocalScale(float scale)
    {
        var inverse = Matrix4x4<float>.Invert(Matrix4x4.CreateScale(scale));

        Assert.That(inverse.M11, Is.EqualTo(1 / scale).Within(1e-6f / scale));
        Assert.That(inverse.M44, Is.EqualTo(1f));
    }

    [Test]
    public void Inverse_OfMixedMagnitudes_UndoesTheMatrix()
    {
        var turn = Matrix4x4.CreateRotationY(0.3f);
        foreach (var m in new[]
                 {
                     Matrix4x4.CreateTranslation(1e10f, 0f, 0f), Matrix4x4.CreateTranslation(1e12f, -3e11f, 5f),
                     Matrix4x4.CreateScale(1e20f, 1f, 1e-20f), turn * Matrix4x4.CreateTranslation(4e9f, 1f, -2e9f),
                     Matrix4x4.CreateLookAtLH(Vector3.Create(1e10f, 20f, -30f), Vector3<float>.Zero, Vector3<float>.UnitY)
                 })
        {
            Assert.That(IsNearEachRow(Matrix4x4<float>.Invert(m).As<double>(), Matrix4x4D.Invert(m.As<double>()), 1e-5), Is.True,
                $"{m}");
        }

        Assert.That(IsNearEachRow(Matrix3x3<float>.Invert(Matrix3x3.CreateScale(1e25f, 1f, 1e-25f)).As<double>(),
            Matrix3x3<double>.Invert(Matrix3x3.CreateScale(1e25, 1, 1e-25)), 1e-5), Is.True);
    }

    [Test]
    public void Inverse_InHalf_OfAMoveAndATurn_UndoesThem()
    {
        foreach (var move in new[] { 16.0, 37.0, 128.0, 1000.0 })
        {
            var m = (Matrix4x4.CreateRotationY(0.3) * Matrix4x4.CreateTranslation(move, -move / 2, 3.0)).As<Half>();

            Assert.That(IsNearEachRow(Matrix4x4<Half>.Invert(m).As<double>(), Matrix4x4D.Invert(m.As<double>()), 4e-3), Is.True,
                $"{move}");
        }
    }

    private static bool IsNearEachRow(Matrix4x4D actual, Matrix4x4D expected, double tolerance)
    {
        for (var r = 0; r < 4; r++)
        {
            var size = Math.Max(Math.Max(Math.Abs(expected[r, 0]), Math.Abs(expected[r, 1])),
                Math.Max(Math.Abs(expected[r, 2]), Math.Abs(expected[r, 3])));
            for (var c = 0; c < 4; c++)
            {
                if (!(Math.Abs(actual[r, c] - expected[r, c]) <= tolerance * size))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsNearEachRow(Matrix3x3<double> actual, Matrix3x3<double> expected, double tolerance) =>
        IsNearEachRow((Matrix4x4D)actual, (Matrix4x4D)expected, tolerance);

    [Test]
    public void TransformNormal_LeavesTheTranslationOut_EvenAnInfiniteOne()
    {
        var move = Matrix4x4.CreateTranslation(double.PositiveInfinity, 0.0, 0.0);

        Assert.That(Vector3<double>.TransformNormal(Vector3<double>.UnitX, move), Is.EqualTo(Vector3<double>.UnitX));
        Assert.That(Vector2<double>.TransformNormal(Vector2<double>.UnitX, move), Is.EqualTo(Vector2<double>.UnitX));
    }

    [Test]
    public void UpperTriangularForm_OfASmallScale_IsTriangular()
    {
        var small = Matrix4x4.CreateScale(1e-7f);
        small.M12 = 3e-7f;
        small.M21 = 5e-7f;

        var upper = Matrix4x4<float>.UpperTriangularForm(small);

        Assert.That(new[] { upper.M21, upper.M31, upper.M32, upper.M41, upper.M42, upper.M43 }, Is.All.EqualTo(0));
    }

    [Test]
    public void Decompose_OfAMirror_GivesANegativeScaleAndAUnitRotation()
    {
        var rotation = Quaternion.CreateFromYawPitchRoll(0.3, -0.2, 0.9);
        var mirror = Matrix4x4.CreateScale(-2.0, 1.0, 3.0) * Matrix4x4.CreateFromQuaternion(rotation) *
                     Matrix4x4.CreateTranslation(1.0, 2.0, 3.0);

        Assert.That(mirror.Decompose(out var scale, out var turn, out var translation), Is.True);

        Assert.That(scale.X, Is.Negative);
        Assert.That(turn.IsNormalized, Is.True);
        var rebuilt = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(translation);
        Assert.That(Matrix4x4D.NearEqual(rebuilt, mirror), Is.True);
    }

    [Test]
    public void Skew_DoesNotDependOnTheVectorsLengths()
    {
        var unit = Matrix4x4.CreateSkew(0.1f, Vector3.Create(1f, 0f, 0f), Vector3.Create(0f, 1f, 0f));
        var tiny = Matrix4x4.CreateSkew(0.1f, Vector3.Create(1e-6f, 0f, 0f), Vector3.Create(0f, 1e-7f, 0f));

        Assert.That(Matrix4x4<float>.NearEqual(tiny, unit), Is.True);
        Assert.Throws<ArgumentException>(() => Matrix4x4.CreateSkew(0.1f, Vector3.Create(1f, 0f, 0f), Vector3<float>.Zero));
        foreach (var (rotation, translation) in new[]
                 {
                     (Vector3.Create(2e19f, 0f, 0f), Vector3.Create(0f, 1f, 0f)),
                     (Vector3.Create(1f, 0f, 0f), Vector3.Create(0f, 2e19f, 0f)),
                     (Vector3.Create(1e-23f, 0f, 0f), Vector3.Create(0f, 1f, 0f))
                 })
        {
            Assert.That(Matrix4x4<float>.NearEqual(Matrix4x4.CreateSkew(0.1f, rotation, translation), unit), Is.True,
                $"{rotation} {translation}");
        }

        var half = Matrix4x4.CreateSkew((Half)0.5, Vector3.Create((Half)1, (Half)0, (Half)0), Vector3.Create((Half)0, (Half)300, (Half)0));
        Assert.That((float)half.M12, Is.EqualTo(MathF.Tan(0.5f)).Within(2e-3f));
    }

    [Test]
    public void ExchangeRowsAndColumns_SwapThem()
    {
        var m = Sample;

        m.ExchangeRows(0, 2);
        m.ExchangeColumns(1, 3);

        Assert.That(m.Row1, Is.EqualTo(new Vector4D(Sample.M31, Sample.M34, Sample.M33, Sample.M32)));
        Assert.That(m.Row3, Is.EqualTo(new Vector4D(Sample.M11, Sample.M14, Sample.M13, Sample.M12)));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.ExchangeRows(0, 4));
    }

    [Test]
    public void SpanTransforms_RejectAPartlyOverlappingDestination()
    {
        var buffer = new Vector4D[4];
        var translation = Matrix4x4.CreateTranslation(1.0, 0.0, 0.0);

        Assert.Throws<ArgumentException>(() => Vector4D.Transform(buffer.AsSpan(0, 3), translation, buffer.AsSpan(1, 3)));
        Assert.DoesNotThrow(() => Vector4D.Transform(buffer, translation, buffer));
        var shared = new Vector3<double>[8];
        Assert.Throws<ArgumentException>(() => Vector3<double>.Transform(shared.AsSpan(0, 4), translation,
            System.Runtime.InteropServices.MemoryMarshal.Cast<Vector3<double>, Vector4D>(shared.AsSpan())));
        Assert.DoesNotThrow(() => Vector4D.Transform(buffer.AsSpan(2, 2), translation, buffer.AsSpan()));
    }

    [Test]
    public void Indexers_ReadRowByRow()
    {
        var m = Sample;

        m[2, 1] = 9;

        Assert.That(m[9], Is.EqualTo(9.0));
        Assert.That(m.M32, Is.EqualTo(9.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = m[16]);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = m[0, 4]);
    }

    [Test]
    public void Layout_IsSixteenElementsRowByRow()
    {
        Assert.That(Matrix4x4<float>.SizeInBytes, Is.EqualTo(64));
        Assert.That(Matrix4x4D.SizeInBytes, Is.EqualTo(128));
        var m = Sample;
        var elements = System.Runtime.InteropServices.MemoryMarshal.Cast<Matrix4x4D, double>(new Span<Matrix4x4D>(ref m));
        Assert.That(elements.ToArray(), Is.EqualTo(Sample.ToArray()));
    }
}
