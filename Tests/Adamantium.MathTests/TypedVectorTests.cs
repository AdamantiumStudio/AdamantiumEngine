using System;
using Adamantium.Mathematics.Typed;
using NUnit.Framework;
using Vector3F = Adamantium.Mathematics.Typed.Vector3<float>;
using Vector3D = Adamantium.Mathematics.Typed.Vector3<double>;
using QuaternionF = Adamantium.Mathematics.Typed.Quaternion<float>;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedVectorTests
{
    [Test]
    public void Create_InfersTheNumberType()
    {
        var floats = Vector3.Create(1f, 2f, 3f);
        var doubles = Vector3.Create(1.0, 2.0, 3.0);
        var ints = Vector3.Create(1, 2, 3);

        Assert.That(floats, Is.TypeOf<Vector3<float>>());
        Assert.That(doubles, Is.TypeOf<Vector3<double>>());
        Assert.That(ints, Is.TypeOf<Vector3<int>>());
    }

    [Test]
    public void IntegerVectors_DoTheirArithmeticExactly()
    {
        var a = Vector3.Create(1, -2, 3);
        var b = Vector3.Create(4, 5, -6);

        Assert.That(a + b, Is.EqualTo(Vector3.Create(5, 3, -3)));
        Assert.That(Vector3<int>.Dot(a, b), Is.EqualTo(4 - 10 - 18));
        Assert.That(Vector3<int>.Cross(a, b), Is.EqualTo(Vector3.Create(-3, 18, 13)));
        Assert.That(Vector3<int>.Max(a), Is.EqualTo(3));
        Assert.That(Vector3<int>.Abs(a), Is.EqualTo(Vector3.Create(1, 2, 3)));
        Assert.That(Vector3.Create(1, 2, 3) == Vector3.Create(1, 2, 4), Is.False);
    }

    [Test]
    public void FractionalMembers_AreReachedThroughAnAlias()
    {
        var v = new Vector3F(3, 0, 4);

        Assert.That(v.Length(), Is.EqualTo(5f));
        Assert.That(Vector3F.Distance(v, Vector3F.Zero), Is.EqualTo(5f));
        Assert.That(Vector3F.Normalize(v), Is.EqualTo(new Vector3F(0.6f, 0, 0.8f)));
        Assert.That(Vector3F.Lerp(Vector3F.Zero, v, 0.5f), Is.EqualTo(new Vector3F(1.5f, 0, 2f)));
    }

    [Test]
    public void Normalize_OnAVariable_ChangesIt()
    {
        var v = new Vector3D(0, 0, 2);

        v.Normalize();

        Assert.That(v.Z, Is.EqualTo(1.0));
        Assert.That(v.IsNormalized, Is.True);
    }

    [Test]
    public void Normalize_LeavesAZeroVectorAsItIs()
    {
        var v = Vector3D.Zero;

        v.Normalize();

        Assert.That(v.IsZero, Is.True);
    }

    [Test]
    public void As_ConvertsEachComponentAsACastWould()
    {
        var precise = Vector3.Create(1.5, -2.75, 1e40);

        Assert.That(precise.As<float>(), Is.EqualTo(Vector3.Create(1.5f, -2.75f, float.PositiveInfinity)));
        Assert.That(Vector3.Create(1.9, -1.9, 0.0).As<int>(), Is.EqualTo(Vector3.Create(1, -1, 0)));
        Assert.That(Vector3.Create(1, 2, 3).As<double>(), Is.EqualTo(Vector3.Create(1.0, 2.0, 3.0)));
    }

    [Test]
    public void Conversions_DropOrAddComponents()
    {
        var v = Vector3.Create(1f, 2f, 3f);

        Assert.That((Vector2<float>)v, Is.EqualTo(Vector2.Create(1f, 2f)));
        Assert.That((Vector4<float>)v, Is.EqualTo(Vector4.Create(1f, 2f, 3f, 0f)));
        Assert.That((Vector3<float>)Vector4.Create(1f, 2f, 3f, 4f), Is.EqualTo(v));
    }

    [Test]
    public void Indexer_ReadsAndWritesComponentsInOrder()
    {
        var v = Vector4.Create(1, 2, 3, 4);

        v[2] = 7;

        Assert.That(v[0] + v[1] + v[2] + v[3], Is.EqualTo(14));
        Assert.That(v.Z, Is.EqualTo(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = v[4]);
    }

    [Test]
    public void Orthonormalize_GivesPerpendicularUnitVectors()
    {
        Span<Vector3F> basis = stackalloc Vector3F[3];

        Vector3F.Orthonormalize(basis, new Vector3F(1, 1, 0), new Vector3F(1, 0, 0), new Vector3F(0, 1, 1));

        Assert.That(Vector3F.Dot(basis[0], basis[1]), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(Vector3F.Dot(basis[0], basis[2]), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(Vector3F.Dot(basis[1], basis[2]), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(basis[2].Length(), Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void Transform_ByAQuarterTurnAroundY_TurnsXIntoMinusZ()
    {
        var turn = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, MathF.PI / 2);

        Assert.That(Vector3F.NearEqual(Vector3F.Transform(Vector3F.UnitX, turn), new Vector3F(0, 0, -1)), Is.True);
    }

    [Test]
    public void YawPitchRoll_RoundTrips()
    {
        var turn = Quaternion.CreateFromYawPitchRoll(0.3f, -0.4f, 1.1f);

        turn.ToYawPitchRoll(out var yaw, out var pitch, out var roll);

        Assert.That(yaw, Is.EqualTo(0.3f).Within(1e-6f));
        Assert.That(pitch, Is.EqualTo(-0.4f).Within(1e-6f));
        Assert.That(roll, Is.EqualTo(1.1f).Within(1e-6f));
    }

    [Test]
    public void QuaternionTimesItsInverse_IsIdentity()
    {
        var turn = QuaternionF.Normalize(new QuaternionF(0.2f, -0.5f, 0.4f, 0.7f));

        Assert.That((turn * QuaternionF.Invert(turn)).IsIdentity, Is.True);
    }

    [Test]
    public void IntegerEquality_IsExactAndNeverOverflows()
    {
        Assert.That(Vector2.Create(int.MaxValue, 0) == Vector2.Create(-1, 0), Is.False);
        Assert.That(Vector2.Create(int.MinValue, 0) == Vector2<int>.Zero, Is.False);
        Assert.That(Vector2.Create(int.MinValue, 0) == Vector2.Create(int.MinValue, 0), Is.True);
    }

    [Test]
    public void Equality_IsExact_NearEqualHasTheTolerance()
    {
        var a = new Vector3F(1, 2, 3);
        var b = new Vector3F(1, 2, 3.0000002f);

        Assert.That(a == b, Is.False);
        Assert.That(a.Equals(b), Is.False);
        Assert.That(Vector3F.NearEqual(a, b), Is.True);
        Assert.That(QuaternionF.NearEqual(QuaternionF.Identity, new QuaternionF(0, 0, 1e-7f, 1)), Is.True);
        Assert.That(new QuaternionF(0, 0, 1e-7f, 1).IsIdentity, Is.False);
    }

    [Test]
    public void NaN_DiffersByOperator_ButEqualsItselfAsAKey()
    {
        var nan = new Vector3F(float.NaN, 0, 0);
        var same = nan;

        Assert.That(nan == same, Is.False);
        Assert.That(nan != same, Is.True);
        Assert.That(nan.Equals(same), Is.True);
        Assert.That(new System.Collections.Generic.HashSet<Vector3F> { nan }.Contains(nan), Is.True);
    }

    [Test]
    public void IntegerNearEqual_MeasuresTheDifference()
    {
        Assert.That(Vector3<int>.NearEqual(new Vector3<int>(1), new Vector3<int>(1000), new Vector3<int>(1)), Is.False);
        Assert.That(Vector3<int>.NearEqual(new Vector3<int>(100), new Vector3<int>(101), new Vector3<int>(1)), Is.True);
    }

    [Test]
    public void Half_HasATolerance()
    {
        var v = Vector3<Half>.Normalize(Vector3.Create((Half)3, (Half)0, (Half)4));

        Assert.That(v.IsNormalized, Is.True);
        Assert.That(MathHelper.Scalar.NearEqual((Half)1, (Half)1.001), Is.True);
    }

    [TestCase(1, 0, 0, 2, 0, 0, true)]
    [TestCase(0, 1, 2, 0, 2, 4, true)]
    [TestCase(1, 2, 3, -2, -4, -6, true)]
    [TestCase(0, 0, 0, 1, 2, 3, true)]
    [TestCase(1, 2, 0, 2, 4, 7, false)]
    [TestCase(1, 0, 0, 0, 1, 0, false)]
    public void IsCollinear_ComparesDirectionsNotRatios(float x1, float y1, float z1, float x2, float y2, float z2, bool collinear)
    {
        Assert.That(new Vector3F(x1, y1, z1).IsCollinear(new Vector3F(x2, y2, z2)), Is.EqualTo(collinear));
    }

    [Test]
    public void IsCollinear_OfTinyVectors_ComparesTheirDirections()
    {
        Assert.That(new Vector3F(1e-12f, 0, 0).IsCollinear(new Vector3F(0, 1e-12f, 0)), Is.False);
        Assert.That(new Vector3F(1e-12f, 0, 0).IsCollinear(new Vector3F(-3e-12f, 0, 0)), Is.True);
        Assert.That(Vector2.Create(1e-30f, 0f).IsCollinear(Vector2.Create(0f, 1e-30f)), Is.False);
        Assert.That(Vector2.Create(float.PositiveInfinity, 0f).IsCollinear(Vector2.Create(1f, 0f)), Is.True);
        Assert.That(new Vector3F(0, float.NegativeInfinity, 0).IsCollinear(new Vector3F(1, 0, 0)), Is.False);
        Assert.That(new Vector3F(0, float.NaN, 0).IsCollinear(new Vector3F(1, 0, 0)), Is.False);
        Assert.That(new Vector3F(float.NaN, 0, 0).IsCollinear(new Vector3F(0, 1, 0)), Is.False);
        Assert.That(Vector2.Create(1f, 0f).IsCollinear(Vector2.Create(0f, float.NaN)), Is.False);
    }

    [Test]
    public void LongNearEqual_IsExact()
    {
        Assert.That(MathHelper.Scalar.NearEqual(9007199254740993L, 9007199254740992L), Is.False);
        Assert.That(MathHelper.Scalar.NearEqual(long.MaxValue, long.MaxValue - 100), Is.False);
        Assert.That(MathHelper.Scalar.WithinEpsilon(long.MaxValue, long.MinValue, 5L), Is.False);
        Assert.That(MathHelper.Scalar.WithinEpsilon(long.MinValue + 3, long.MinValue, 5L), Is.True);
    }

    [TestCase(1, 0, 2, 0, true)]
    [TestCase(1, 2, -2, -4, true)]
    [TestCase(0, 0, 1, 2, true)]
    [TestCase(1, 0, 0, 1, false)]
    [TestCase(1e-4f, 0, 0, 1e-4f, false)]
    public void IsCollinear_InTwoDimensions_IsRelativeToTheLengths(float x1, float y1, float x2, float y2, bool collinear)
    {
        Assert.That(Vector2.Create(x1, y1).IsCollinear(Vector2.Create(x2, y2)), Is.EqualTo(collinear));
    }

    [Test]
    public void Slerp_OfPerpendicularRotations_StaysOnTheSphere()
    {
        var result = QuaternionF.Slerp(QuaternionF.Identity, new QuaternionF(1, 0, 0, 0), 1);

        Assert.That(result, Is.EqualTo(new QuaternionF(1, 0, 0, 0)));
    }

    [Test]
    public void Slerp_OfNaN_GivesNaN()
    {
        var result = QuaternionF.Slerp(new QuaternionF(float.NaN), QuaternionF.Identity, 0.5f);

        Assert.That(float.IsNaN(result.W), Is.True);
    }

    [Test]
    public void SignedDirections_AreThereForSignedTypes()
    {
        Assert.That(Vector3<int>.Down, Is.EqualTo(Vector3.Create(0, -1, 0)));
        Assert.That(Vector3F.ForwardRH, Is.EqualTo(new Vector3F(0, 0, -1)));
    }

    [Test]
    public void Layout_IsTheComponentsBackToBack()
    {
        Assert.That(Vector3F.SizeInBytes, Is.EqualTo(12));
        Assert.That(Vector3D.SizeInBytes, Is.EqualTo(24));
        Assert.That(Vector4<double>.SizeInBytes, Is.EqualTo(32));
        Assert.That(QuaternionF.SizeInBytes, Is.EqualTo(16));
    }
}
