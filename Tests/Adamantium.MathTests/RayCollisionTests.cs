using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class RayCollisionTests
{
    [Test]
    public void RaysWhoseLinesCrossBehindAnOrigin_DoNotIntersect()
    {
        var first = new Ray<float>(new Vector3F(0, 0, 0), Vector3F.UnitX);
        var second = new Ray<float>(new Vector3F(-5, 5, 0), Vector3F.UnitY);

        Assert.That(Collision.RayIntersectsRay(first, second, out _), Is.False);
    }

    [Test]
    public void RaysThatCrossAhead_IntersectWhereTheyCross()
    {
        var first = new Ray<float>(new Vector3F(0, 0, 0), Vector3F.UnitX);
        var second = new Ray<float>(new Vector3F(5, -5, 0), Vector3F.UnitY);

        Assert.That(Collision.RayIntersectsRay(first, second, out var point), Is.True);
        Assert.That((point - new Vector3F(5, 0, 0)).Length(), Is.LessThan(1e-4f));
    }

    [Test]
    public void RaysAlongOneLine_MeetWhereTheSecondStarts()
    {
        var first = new Ray<float>(new Vector3F(0, 0, 0), Vector3F.UnitX);
        var second = new Ray<float>(new Vector3F(3, 0, 0), -Vector3F.UnitX);

        Assert.That(Collision.RayIntersectsRay(first, second, out var point), Is.True);
        Assert.That((point - new Vector3F(3, 0, 0)).Length(), Is.LessThan(1e-4f));
    }

    [Test]
    public void ATinyTriangle_IsStillHit()
    {
        var ray = new Ray<float>(new Vector3F(0, 0, -1), Vector3F.UnitZ);
        var a = new Vector3F(-2e-4f, -2e-4f, 0);
        var b = new Vector3F(2e-4f, -2e-4f, 0);
        var c = new Vector3F(0, 2e-4f, 0);

        Assert.That(Collision.RayIntersectsTriangle(ray, a, b, c, out float distance), Is.True);
        Assert.That(distance, Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void AShortSegmentAcrossTheRay_IsTouched()
    {
        var ray = new Ray<float>(new Vector3F(0, 0, 0), Vector3F.UnitZ);

        var gap = Collision.RayIntersectsLineSegment(ray, new Vector3F(-1e-5f, 0, 5), new Vector3F(1e-5f, 0, 5), out var point);

        Assert.That(gap, Is.LessThan(1e-6f));
        Assert.That(point.Z, Is.EqualTo(5f).Within(1e-5f));
    }
}
