using System;
using Adamantium.Mathematics;
using NUnit.Framework;
using Vector3D = Adamantium.Mathematics.Vector3<double>;

namespace Adamantium.MathTests;

[TestFixture]
public class TypedFrustumTests
{
    private const double Near = 1;
    private const double Far = 100;

    [Test]
    public void APointBeforeTheNearPlane_IsOutside()
    {
        var frustum = Perspective();

        Assert.That(frustum.Contains(Vector3.Create(0, 0, Near * 0.6)), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(frustum.Contains(Vector3.Create(0, 0, Near * 1.1)), Is.EqualTo(ContainmentType.Contains));
    }

    [Test]
    public void TheFarPlane_IsCheckedOnlyWhenAsked()
    {
        var beyond = Vector3.Create(0, 0, Far * 2);

        Assert.That(Perspective().Contains(beyond), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(BoundingFrustum.Create(Matrix4x4.CreatePerspectiveFovY(Math.PI / 2, 1, Near, Far)).Contains(beyond),
            Is.EqualTo(ContainmentType.Contains));
    }

    [Test]
    public void ABoxAcrossOnePlane_ButBeyondAnother_IsDisjoint()
    {
        var box = BoundingBox.Create(Vector3.Create(-30.0, 40, 10), Vector3.Create(-5.0, 45, 12));

        Assert.That(Perspective().Contains(box), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void ABoxInside_IsContained_AndOneAcrossAPlane_Intersects()
    {
        var frustum = Perspective();

        Assert.That(frustum.Contains(BoundingBox.Create(Vector3.Create(-1.0, -1, 10), Vector3.Create(1.0, 1, 12))), Is.EqualTo(ContainmentType.Contains));
        Assert.That(frustum.Contains(BoundingBox.Create(Vector3.Create(-20.0, -1, 10), Vector3.Create(1.0, 1, 12))), Is.EqualTo(ContainmentType.Intersects));
    }

    [Test]
    public void PointsAcrossAPlane_Intersect_AndNoPoints_AreOutside()
    {
        var frustum = Perspective();

        Assert.That(frustum.Contains([Vector3.Create(0.0, 0, 10), Vector3.Create(-50.0, 0, 10)]), Is.EqualTo(ContainmentType.Intersects));
        Assert.That(frustum.Contains([Vector3.Create(0.0, 0, 10), Vector3.Create(1.0, 0, 10)]), Is.EqualTo(ContainmentType.Contains));
        Assert.That(frustum.Contains([Vector3.Create(-50.0, 0, 10), Vector3.Create(-60.0, 0, 10)]), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(frustum.Contains(ReadOnlySpan<Vector3D>.Empty), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void AFrustumInsideAnother_IsContained_AndOneFarAway_IsDisjoint()
    {
        var outer = Perspective();
        var inner = BoundingFrustum.Create(Matrix4x4.CreatePerspectiveFovY(Math.PI / 4, 1, 2.0, 50), true);
        var away = BoundingFrustum.Create(Matrix4x4.CreateTranslation(0, 0, 500.0) * Matrix4x4.CreatePerspectiveFovY(Math.PI / 4, 1, 2.0, 50), true);

        Assert.That(outer.Contains(inner), Is.EqualTo(ContainmentType.Contains));
        Assert.That(outer.Contains(outer), Is.EqualTo(ContainmentType.Contains));
        Assert.That(outer.Contains(away), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(inner.Intersects(outer), Is.True);
    }

    [Test]
    public void Corners_LieWhereTheProjectionSees_InTheirOrder()
    {
        const double aspect = 2;
        var frustum = BoundingFrustum.Create(Matrix4x4.CreatePerspectiveFovY(Math.PI / 2, aspect, Near, Far), true);
        Span<Vector3D> corners = stackalloc Vector3D[BoundingFrustum<double>.CornerCount];
        frustum.GetCorners(corners);

        Vector3D[] expected =
        [
            Vector3.Create(-Near * aspect, Near, Near), Vector3.Create(Near * aspect, Near, Near),
            Vector3.Create(Near * aspect, -Near, Near), Vector3.Create(-Near * aspect, -Near, Near),
            Vector3.Create(-Far * aspect, Far, Far), Vector3.Create(Far * aspect, Far, Far),
            Vector3.Create(Far * aspect, -Far, Far), Vector3.Create(-Far * aspect, -Far, Far)
        ];
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.That((corners[i] - expected[i]).Length(), Is.LessThan(1e-12 * Far), $"corner {i}");
        }

        Assert.That(frustum.Support(Vector3.Create(1.0, 1, 1)), Is.EqualTo(corners[5]));
        Assert.Throws<ArgumentOutOfRangeException>(() => frustum.GetCorners(new Vector3D[BoundingFrustum<double>.CornerCount - 1]));
    }

    [Test]
    public void Corners_OfManyCameras_AreWhereTheInverseMatrixSendsTheClipCube()
    {
        var view = Matrix4x4.CreateLookAtLH(Vector3.Create(1000.0, 50, -300), Vector3.Create(990.0, 52, -290), Vector3D.UnitY);
        Matrix4x4<double>[] projections =
        [
            Matrix4x4.CreatePerspectiveFovY(1.2, 1.5, 0.1, 5000.0),
            Matrix4x4.CreatePerspectiveFovRH(0.01, 0.75, 1.0, 1e5),
            Matrix4x4.CreateOrthoLH(40.0, 30, 0.5, 900),
            Matrix4x4.CreateOrthoOffCenterLH(-3.0, 17, -2, 9, 1, 50),
            Matrix4x4.CreatePerspectiveOffCenterLH(-3.0, 1, -1, 1, 1, 100),
            Matrix4x4.CreatePerspectiveFovY(1.0, 1, 1.0, 10) * Matrix4x4.CreateScale(1.0, -1, 1)
        ];
        Span<Vector3D> corners = stackalloc Vector3D[BoundingFrustum<double>.CornerCount];
        Span<Vector3<float>> singleCorners = stackalloc Vector3<float>[BoundingFrustum<float>.CornerCount];
        foreach (var projection in projections)
        {
            var viewProjection = view * projection;
            var frustum = BoundingFrustum.Create(viewProjection, true);
            frustum.GetCorners(corners);
            BoundingFrustum.Create(viewProjection.As<float>(), true).GetCorners(singleCorners);
            var inverse = Matrix4x4<double>.Invert(viewProjection);
            var singleInverse = Matrix4x4<double>.Invert(viewProjection.As<float>().As<double>());
            for (var i = 0; i < corners.Length; i++)
            {
                var (x, y, z) = ((i & 3) is 0 or 3 ? -1 : 1, (i & 3) < 2 ? 1 : -1, i < 4 ? 0 : 1);
                var expected = Unproject(inverse, x, y, z);
                Assert.That((corners[i] - expected).Length(), Is.LessThan(1e-9 * Math.Max(1, expected.Length())), $"corner {i} of {projection}");
                var singleExpected = Unproject(singleInverse, x, y, z);
                Assert.That((singleCorners[i].As<double>() - singleExpected).Length(), Is.LessThan(1e-5 * Math.Max(1, singleExpected.Length())),
                    $"float corner {i} of {projection}");
            }

            Assert.That(frustum.Contains(Unproject(inverse, 0.3, -0.2, 0.5)), Is.EqualTo(ContainmentType.Contains));
            Assert.That(frustum.Contains(Unproject(inverse, 1.2, 0, 0.5)), Is.EqualTo(ContainmentType.Disjoint));
            Assert.That(Plane<double>.DotCoordinate(frustum.Top, Unproject(inverse, 0, 1.2, 0.5)), Is.LessThan(0));
        }
    }

    [Test]
    public void InHalf_ACamerasFrustum_AnswersAsInDouble()
    {
        var viewProjection = Matrix4x4.CreateLookAtLH(Vector3.Create(2.0, 1, -3), Vector3D.Zero, Vector3D.UnitY)
                             * Matrix4x4.CreatePerspectiveFovY(1.0, 1.5, 0.5, 20);
        var half = BoundingFrustum.Create(viewProjection.As<Half>(), true);
        var exact = BoundingFrustum.Create(viewProjection, true);
        Span<Vector3<Half>> halfCorners = stackalloc Vector3<Half>[BoundingFrustum<Half>.CornerCount];
        Span<Vector3D> corners = stackalloc Vector3D[BoundingFrustum<double>.CornerCount];
        half.GetCorners(halfCorners);
        exact.GetCorners(corners);

        for (var i = 0; i < corners.Length; i++)
        {
            Assert.That((halfCorners[i].As<double>() - corners[i]).Length(), Is.LessThan(0.05 * Math.Max(1, corners[i].Length())), $"corner {i}");
        }

        Assert.That(half.Contains(Vector3.Create((Half)0, (Half)0, (Half)0)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(half.Contains(Vector3.Create((Half)0, (Half)0, (Half)(-5))), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void AnEndlessFarPlane_KeepsEverythingAheadInside_ItsFarCornersAtInfinity()
    {
        var frustum = BoundingFrustum.Create(Endless(Math.PI / 2, Near), true);
        Span<Vector3D> corners = stackalloc Vector3D[BoundingFrustum<double>.CornerCount];
        frustum.GetCorners(corners);

        Assert.That(frustum.Far.Normal, Is.EqualTo(Vector3D.Zero));
        Assert.That(frustum.Contains(Vector3.Create(0, 0, 1e300)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(frustum.Contains(BoundingSphere.Create(Vector3.Create(0, 0, 1e30), 1.0)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(frustum.Contains(BoundingBox.Create(Vector3.Create(-1.0, -1, 1e10), Vector3.Create(1.0, 1, 1e12))), Is.EqualTo(ContainmentType.Contains));
        Assert.That(Vector3D.NearEqual(corners[0], Vector3.Create(-Near, Near, Near)), Is.True);
        Assert.That(corners[4], Is.EqualTo(Vector3.Create(double.NegativeInfinity, double.PositiveInfinity, double.PositiveInfinity)));
        Assert.That(corners[6], Is.EqualTo(Vector3.Create(double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity)));
    }

    [Test]
    public void AnEndlessFrustum_ReachesAndMeetsWhatLiesAhead()
    {
        var endless = BoundingFrustum.Create(Endless(Math.PI / 2, Near), true);
        var finite = BoundingFrustum.Create(Matrix4x4.CreatePerspectiveFovY(Math.PI / 2, 1, Near, Far));
        var wider = BoundingFrustum.Create(Matrix4x4.CreatePerspectiveFovY(Math.PI * 0.6, 1, Near / 2, Far));
        var crossing = BoundingFrustum.Create(
            Matrix4x4.CreateLookAtLH(Vector3.Create(-500.0, 0, 50), Vector3.Create(-400.0, 0, 50), Vector3D.UnitY) * Endless(Math.PI / 2, Near), true);
        var behind = BoundingFrustum.Create(
            Matrix4x4.CreateLookAtLH(Vector3.Create(0.0, 0, -10), Vector3.Create(0.0, 0, -20), Vector3D.UnitY) * Endless(Math.PI / 4, Near), true);

        Assert.That(endless.Support(Vector3D.UnitZ).Z, Is.EqualTo(double.PositiveInfinity));
        Assert.That(endless.Support(-Vector3D.UnitZ).Z, Is.EqualTo(Near));
        Assert.That(endless.Intersects(Plane.Create(Vector3D.UnitZ, -0.5)), Is.EqualTo(PlaneIntersectionType.Front));
        Assert.That(endless.Intersects(Plane.Create(Vector3D.UnitZ, -50.0)), Is.EqualTo(PlaneIntersectionType.Intersecting));
        Assert.That(endless.Intersects(Plane.Create(-Vector3D.UnitZ, 0.5)), Is.EqualTo(PlaneIntersectionType.Back));
        Assert.That(endless.Intersects(Plane.Create(Vector3D.UnitX, -1e6)), Is.EqualTo(PlaneIntersectionType.Intersecting));
        Assert.That(finite.Intersects(crossing), Is.True);
        Assert.That(wider.Contains(endless), Is.EqualTo(ContainmentType.Contains));
        Assert.That(finite.Contains(behind), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void AZeroMatrix_SeesNothing()
    {
        var zero = BoundingFrustum.Create(Matrix4x4<double>.Zero, true);

        Assert.That(zero.Contains(Vector3.Create(5.0, -7, 3)), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(zero.Contains(BoundingSphere.Create(Vector3D.Zero, 100.0)), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(zero.Contains(UnitBox()), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(Perspective().Intersects(zero), Is.False);
    }

    [Test]
    public void APlaneOfHugeCoefficients_IsKept()
    {
        var single = Matrix4x4.CreatePerspectiveOffCenterLH(-3f, 1, -1, 1, 1, 100);
        var huge = BoundingFrustum.Create(single * 3e38f);

        Assert.That(float.IsFinite(huge.Left.D) && Math.Abs(huge.Left.Normal.Length() - 1) < 1e-6, Is.True);
        Assert.That(huge.Contains(Vector3.Create(-1f, 0, 10)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(huge.Contains(Vector3.Create(-40f, 0, 10)), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void TinyMatrices_AndTinyNextToHuge_KeepTheirPlanes()
    {
        var single = Matrix4x4.CreatePerspectiveFovY(MathF.PI / 2, 1, 1f, 100);
        var mixed = Matrix4x4<float>.Identity;
        mixed.M11 = 3e38f;
        mixed.M22 = mixed.M33 = mixed.M44 = 1e-7f;
        var half = (Matrix4x4.CreatePerspectiveFovY(Math.PI / 2, 1, 1.0, 100) * 1e-5).As<Half>();

        Assert.That(BoundingFrustum.Create(single * 4e-39f).Contains(Vector3.Create(0f, 0, 10)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(BoundingFrustum.Create(single * 1e-44f).Contains(Vector3.Create(0f, 0, 10)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(BoundingFrustum.Create(mixed).Contains(Vector3.Create(0f, 0.5f, 0.5f)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(BoundingFrustum.Create(half).Contains(Vector3.Create((Half)0, (Half)0, (Half)10)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(BoundingFrustum.Create(half).Contains(Vector3.Create((Half)0, (Half)0, (Half)(-10))), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void TheSupportPoint_SkipsAnEdgePerpendicularToTheDirection()
    {
        var endless = BoundingFrustum.Create(Endless(Math.PI / 2, Near), true);
        Span<Vector3D> corners = stackalloc Vector3D[BoundingFrustum<double>.CornerCount];
        endless.GetCorners(corners);

        Assert.That(endless.Support(Vector3.Create(1.0, 0, 1)), Is.EqualTo(corners[5]).Or.EqualTo(corners[6]));
    }

    [Test]
    public void BothDepthPlanesAtInfinity_LeaveEdgesOfFiniteSides()
    {
        var constantDepth = Matrix4x4<double>.Zero;
        constantDepth.M11 = constantDepth.M22 = constantDepth.M44 = 1;
        constantDepth.M43 = 0.5;
        var frustum = BoundingFrustum.Create(constantDepth, true);
        Span<Vector3D> corners = stackalloc Vector3D[BoundingFrustum<double>.CornerCount];
        frustum.GetCorners(corners);

        Assert.That(frustum.Contains(Vector3.Create(0, 0, 7.0)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(frustum.Contains(Vector3.Create(2, 0, 7.0)), Is.EqualTo(ContainmentType.Disjoint));
        foreach (var corner in corners)
        {
            Assert.That(Math.Abs(corner.X) == 1 && Math.Abs(corner.Y) == 1 && double.IsInfinity(corner.Z), Is.True, $"{corner}");
        }
    }

    [Test]
    public void TheDefault_IsTheZeroMatrixFrustum_SeeingNothing()
    {
        var zero = BoundingFrustum.Create(Matrix4x4<double>.Zero);

        Assert.That(default(BoundingFrustum<double>) == zero, Is.True);
        Assert.That(default(BoundingFrustum<double>).Contains(Vector3.Create(1.0, 2, 3)), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(default(BoundingFrustum<double>).Contains(BoundingSphere.Create(Vector3D.Zero, 5.0)), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(default(BoundingFrustum<double>).Contains(UnitBox()), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void ABoxEndlessAlongAnAxis_ThroughTheView_Crosses()
    {
        var box = BoundingBox.Create(Vector3.Create(double.NegativeInfinity, -1, 5), Vector3.Create(double.PositiveInfinity, 1.0, 6));

        Assert.That(Perspective().Contains(box), Is.EqualTo(ContainmentType.Intersects));
        Assert.That(Plane.Create(Vector3D.UnitZ, -4.0).Intersects(box), Is.EqualTo(PlaneIntersectionType.Front));
    }

    [Test]
    public void ANaN_IsOutside()
    {
        var frustum = Perspective();
        var nan = double.NaN;

        Assert.That(frustum.Contains(Vector3.Create(0, 0, nan)), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(frustum.Contains(BoundingBox.Create(Vector3.Create(-1.0, -1, nan), Vector3.Create(1.0, 1, 12))), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(frustum.Contains(BoundingBox.Create(Vector3.Create(-1.0, -1, 10), Vector3.Create(1.0, nan, 12))), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(frustum.Contains(BoundingSphere.Create(Vector3.Create(0.0, 0, 10), nan)), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(frustum.Contains([Vector3.Create(0.0, 0, 10), Vector3.Create(nan, 0, 10)]), Is.EqualTo(ContainmentType.Intersects));
        Assert.That(BoundingFrustum.Create(Matrix4x4.CreateTranslation(nan, 0, 0) * Matrix4x4.CreatePerspectiveFovY(Math.PI / 2, 1, Near, Far))
            .Contains(Vector3.Create(0, 0, 10.0)), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void ASphereTouchingAPlane_IsInsideFromInside_AndCrossesFromOutside()
    {
        var frustum = BoundingFrustum.Create(Matrix4x4<double>.Identity, true);

        Assert.That(frustum.Contains(BoundingSphere.Create(Vector3.Create(0.5, 0, 0.5), 0.5)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(frustum.Contains(BoundingSphere.Create(Vector3.Create(1.5, 0, 0.5), 0.5)), Is.EqualTo(ContainmentType.Intersects));
        Assert.That(frustum.Contains(BoundingSphere.Create(Vector3.Create(1.0, 0, 0.5), 0.0)), Is.EqualTo(ContainmentType.Contains));
        Assert.That(frustum.Contains(BoundingSphere.Create(Vector3.Create(2.0, 0, 0.5), 0.5)), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void APlane_IsCrossedOrNot_ByTheCorners()
    {
        var frustum = Perspective();

        Assert.That(frustum.Intersects(Plane.Create(Vector3D.UnitZ, -50.0)), Is.EqualTo(PlaneIntersectionType.Intersecting));
        Assert.That(frustum.Intersects(Plane.Create(Vector3D.UnitZ, 0.5)), Is.EqualTo(PlaneIntersectionType.Front));
        Assert.That(frustum.Intersects(Plane.Create(Vector3D.UnitZ, -200.0)), Is.EqualTo(PlaneIntersectionType.Back));
    }

    [Test]
    public void Equality_TakesTheFarCheckIn_AndAsKeepsIt()
    {
        var checking = Perspective();
        var notChecking = BoundingFrustum.Create(checking.ViewProjection);

        Assert.That(checking == notChecking, Is.False);
        Assert.That(checking.Equals(notChecking), Is.False);
        var nudged = checking.ViewProjection;
        nudged.M43 += 1e-14;
        Assert.That(BoundingFrustum<double>.NearEqual(checking, BoundingFrustum.Create(nudged, true)), Is.True);
        Assert.That(BoundingFrustum<double>.NearEqual(checking, notChecking), Is.False);
        Assert.That(checking.As<float>().EnableFarPlaneCheck, Is.True);
    }

    [Test]
    public void LargeMatrices_InFloat_AnswerAsInDouble()
    {
        var random = new Random(176);
        var checkedCorners = 0;
        Span<Vector3<float>> singleCorners = stackalloc Vector3<float>[BoundingFrustum<float>.CornerCount];
        for (var n = 0; n < 2000; n++)
        {
            var matrix = new Matrix4x4<double>(Row(random), Row(random), Row(random), Row(random)).As<float>().As<double>();
            var single = BoundingFrustum.Create(matrix.As<float>(), true);
            var exact = BoundingFrustum.Create(matrix, true);
            var inverse = Matrix4x4<double>.Invert(matrix);
            var singlePlanes = Array.ConvertAll(Planes(single), p => p.As<double>());
            var exactPlanes = Planes(exact);
            for (var p = 0; p < exactPlanes.Length; p++)
            {
                Assert.That((singlePlanes[p].Normal - exactPlanes[p].Normal).Length(), Is.LessThan(1e-5), $"plane {p} of {matrix}");
                Assert.That(Math.Abs(singlePlanes[p].D - exactPlanes[p].D), Is.LessThan(1e-5 * Math.Max(1, Math.Abs(exactPlanes[p].D))));
            }

            single.GetCorners(singleCorners);
            for (var c = 0; c < singleCorners.Length; c++)
            {
                if (Conditioned(exact, c))
                {
                    checkedCorners++;
                    var expected = Unproject(inverse, (c & 3) is 0 or 3 ? -1 : 1, (c & 3) < 2 ? 1 : -1, c < 4 ? 0 : 1);
                    Assert.That((singleCorners[c].As<double>() - expected).Length(),
                        Is.LessThan(1e-4 * Math.Max(1, expected.Length())), $"corner {c} of {matrix}");
                }
            }
        }

        Assert.That(checkedCorners, Is.GreaterThan(1000));
    }

    private static Vector3D Unproject(Matrix4x4<double> inverse, double x, double y, double z)
    {
        var point = inverse.Row1 * x + inverse.Row2 * y + inverse.Row3 * z + inverse.Row4;
        return Vector3.Create(point.X, point.Y, point.Z) / point.W;
    }

    private static Matrix4x4<double> Endless(double fieldOfView, double near)
    {
        var endless = Matrix4x4.CreatePerspectiveFovY(fieldOfView, 1, near, Far);
        endless.M33 = 1;
        endless.M43 = -near;
        return endless;
    }

    private static BoundingBox<double> UnitBox() => BoundingBox.Create(Vector3.Create(-1.0, -1, -1), Vector3.Create(1.0, 1, 1));

    private static Vector4<double> Row(Random random) =>
        new((random.NextDouble() * 8 - 4) * 1e30, (random.NextDouble() * 8 - 4) * 1e30, (random.NextDouble() * 8 - 4) * 1e30,
            (random.NextDouble() * 8 - 4) * 1e30);

    private static Plane<T>[] Planes<T>(BoundingFrustum<T> frustum) where T : unmanaged, System.Numerics.IFloatingPointIeee754<T> =>
        [frustum.Near, frustum.Far, frustum.Left, frustum.Right, frustum.Top, frustum.Bottom];

    private static bool Conditioned(BoundingFrustum<double> frustum, int corner)
    {
        var planes = Planes(frustum);
        int[][] meeting = [[0, 2, 4], [0, 3, 4], [0, 3, 5], [0, 2, 5], [1, 2, 4], [1, 3, 4], [1, 3, 5], [1, 2, 5]];
        var (a, b, c) = (planes[meeting[corner][0]].Normal, planes[meeting[corner][1]].Normal, planes[meeting[corner][2]].Normal);
        return Math.Abs(Vector3D.Dot(a, Vector3D.Cross(b, c))) > 0.1;
    }

    private static BoundingFrustum<double> Perspective() =>
        BoundingFrustum.Create(Matrix4x4.CreatePerspectiveFovY(Math.PI / 2, 1, Near, Far), true);
}
