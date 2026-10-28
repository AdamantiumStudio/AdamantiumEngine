using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class BoundingFrustumTests
{
    private const float Near = 1;
    private const float Far = 100;

    [Test]
    public void APointBeforeTheNearPlane_IsOutside()
    {
        var frustum = Perspective();
        var before = new Vector3F(0, 0, Near * 0.6f);
        var after = new Vector3F(0, 0, Near * 1.1f);

        Assert.That(frustum.Contains(before), Is.EqualTo(ContainmentType.Disjoint));
        Assert.That(frustum.Contains(after), Is.EqualTo(ContainmentType.Contains));
    }

    [Test]
    public void ABoxAcrossOnePlane_ButBeyondAnother_IsDisjoint()
    {
        var frustum = Perspective();
        var box = new BoundingBox<float>(new Vector3F(-30, 40, 10), new Vector3F(-5, 45, 12));

        Assert.That(frustum.Contains(box), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void ABoxInside_IsContained_AndOneAcrossAPlane_Intersects()
    {
        var frustum = Perspective();

        Assert.That(frustum.Contains(new BoundingBox<float>(new Vector3F(-1, -1, 10), new Vector3F(1, 1, 12))), Is.EqualTo(ContainmentType.Contains));
        Assert.That(frustum.Contains(new BoundingBox<float>(new Vector3F(-20, -1, 10), new Vector3F(1, 1, 12))), Is.EqualTo(ContainmentType.Intersects));
    }

    [Test]
    public void PointsAcrossAPlane_Intersect()
    {
        var frustum = Perspective();

        Assert.That(frustum.Contains([new Vector3F(0, 0, 10), new Vector3F(-50, 0, 10)]), Is.EqualTo(ContainmentType.Intersects));
        Assert.That(frustum.Contains([new Vector3F(0, 0, 10), new Vector3F(1, 0, 10)]), Is.EqualTo(ContainmentType.Contains));
        Assert.That(frustum.Contains([new Vector3F(-50, 0, 10), new Vector3F(-60, 0, 10)]), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void AFrustumInsideAnother_IsContained_AndOneFarAway_IsDisjoint()
    {
        var outer = Perspective();
        var inner = new BoundingFrustum<float>(Matrix4x4.CreatePerspectiveFovY<float>((MathF.PI / 4), 1, 2, 50), true);
        var away = new BoundingFrustum<float>(Matrix4x4.CreateTranslation<float>(0, 0, 500) * Matrix4x4.CreatePerspectiveFovY<float>((MathF.PI / 4), 1, 2, 50), true);

        Assert.That(outer.Contains(inner), Is.EqualTo(ContainmentType.Contains));
        Assert.That(outer.Contains(away), Is.EqualTo(ContainmentType.Disjoint));
    }

    private static BoundingFrustum<float> Perspective()
    {
        return new BoundingFrustum<float>(Matrix4x4.CreatePerspectiveFovY<float>((MathF.PI / 2), 1, Near, Far), true);
    }
}
