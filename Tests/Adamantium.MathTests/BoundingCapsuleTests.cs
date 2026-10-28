using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class BoundingCapsuleTests
{
    private static readonly BoundingCapsule Upright = new(new Vector3F(0, -1, 0), new Vector3F(0, 1, 0), 0.5f);

    [TestCase(-5f, 0f, 0f, 1f, 0f, 0f, 4.5f)]
    [TestCase(0f, 5f, 0f, 0f, -1f, 0f, 3.5f)]
    [TestCase(0.2f, 5f, 0f, 0f, -1f, 0f, 3.5417424f)]
    [TestCase(-5f, 1.4f, 0f, 1f, 0f, 0f, 4.7f)]
    public void ARay_MeetsTheBodyOrAnEnd(float x, float y, float z, float dx, float dy, float dz, float expected)
    {
        var capsule = Upright;
        var ray = new Ray<float>(new Vector3F(x, y, z), new Vector3F(dx, dy, dz));

        Assert.That(capsule.Intersects(ray, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(expected).Within(1e-4f));
    }

    [Test]
    public void ARayPastTheSide_Misses()
    {
        var capsule = Upright;
        var ray = new Ray<float>(new Vector3F(-5, 0, 2), Vector3F.UnitX);

        Assert.That(capsule.Intersects(ray, out float _), Is.False);
    }

    [Test]
    public void ARayFromInside_MeetsItAtItsOrigin()
    {
        var capsule = Upright;
        var ray = new Ray<float>(new Vector3F(0.1f, 1.2f, 0), Vector3F.UnitX);

        Assert.That(capsule.Intersects(ray, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(0f));
    }

    [Test]
    public void ACapsuleOfNoLength_IsASphere()
    {
        var capsule = new BoundingCapsule(new Vector3F(0, 0, 3), new Vector3F(0, 0, 3), 1);
        var ray = new Ray<float>(Vector3F.Zero, Vector3F.UnitZ);

        Assert.That(capsule.Intersects(ray, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(2f).Within(1e-5f));
    }

    [Test]
    public void TwoCapsules_OverlapOnlyWhenTheirSegmentsComeWithinBothRadii()
    {
        var first = Upright;
        var near = new BoundingCapsule(new Vector3F(0.9f, -1, 0), new Vector3F(0.9f, 1, 0), 0.5f);
        var apart = new BoundingCapsule(new Vector3F(1.2f, -1, 0), new Vector3F(1.2f, 1, 0), 0.5f);
        var across = new BoundingCapsule(new Vector3F(-2, 0.5f, 0.8f), new Vector3F(2, 0.5f, 0.8f), 0.4f);

        Assert.That(first.Intersects(near), Is.True);
        Assert.That(first.Intersects(apart), Is.False);
        Assert.That(first.Intersects(across), Is.True);
    }

    [Test]
    public void ACapsuleAndASphere_OverlapNearTheSegment()
    {
        var capsule = Upright;
        var touching = new BoundingSphere<float>(new Vector3F(0, 2, 0), 0.6f);
        var apart = new BoundingSphere<float>(new Vector3F(0, 2, 0), 0.4f);

        Assert.That(capsule.Intersects(touching), Is.True);
        Assert.That(capsule.Intersects(apart), Is.False);
    }

    [Test]
    public void AFrustum_ContainsCrossesOrMissesACapsule()
    {
        var frustum = new BoundingFrustum<float>(Matrix4x4.CreatePerspectiveFovY<float>((MathF.PI / 2), 1, 1, 100), true);
        var inside = new BoundingCapsule(new Vector3F(0, -1, 10), new Vector3F(0, 1, 10), 0.5f);
        var across = new BoundingCapsule(new Vector3F(-20, 0, 10), new Vector3F(0, 0, 10), 0.5f);
        var behind = new BoundingCapsule(new Vector3F(0, -1, -10), new Vector3F(0, 1, -10), 0.5f);

        Assert.That(inside.Within(frustum), Is.EqualTo(ContainmentType.Contains));
        Assert.That(across.Within(frustum), Is.EqualTo(ContainmentType.Intersects));
        Assert.That(behind.Within(frustum), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void Transform_MovesTheEnds_AndScalesTheRadius()
    {
        var moved = Upright.Transform(Matrix4x4.CreateScale<float>(2) * Matrix4x4.CreateTranslation<float>(1, 2, 3));

        Assert.That((moved.Start - new Vector3F(1, 0, 3)).Length(), Is.LessThan(1e-5f));
        Assert.That((moved.End - new Vector3F(1, 4, 3)).Length(), Is.LessThan(1e-5f));
        Assert.That(moved.Radius, Is.EqualTo(1f).Within(1e-5f));
    }
}
