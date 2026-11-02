using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

/// <summary>A point a distance along a polyline, and the way it runs there.</summary>
public class PolylinePointAtTests
{
    private static Vector2<double> V(double x, double y) => new(x, y);

    // Right 10, then down 10, with a repeated point at the corner.
    private static readonly Vector2<double>[] Corner = [V(0, 0), V(10, 0), V(10, 0), V(10, 10)];

    [TestCase(4, 4, 0, 1, 0)]
    [TestCase(10, 10, 0, 1, 0)]
    [TestCase(13, 10, 3, 0, 1)]
    [TestCase(-5, 0, 0, 1, 0)]
    [TestCase(25, 10, 10, 0, 1)]
    public void APointAlong_IsOnItsSegment_TurnedAsItRuns(double distance, double x, double y, double dx, double dy)
    {
        var point = MathHelper.Polyline.PointAt<double>(Corner, distance, out var direction);

        Assert.Multiple(() =>
        {
            Assert.That(point, Is.EqualTo(V(x, y)));
            Assert.That(direction, Is.EqualTo(V(dx, dy)));
        });
    }

    [Test]
    public void APolylineOfNoLength_GivesItsFirstPoint()
    {
        var point = MathHelper.Polyline.PointAt<double>([V(3, 4), V(3, 4)], 5, out var direction);

        Assert.Multiple(() =>
        {
            Assert.That(point, Is.EqualTo(V(3, 4)));
            Assert.That(direction, Is.EqualTo(V(0, 0)));
        });
    }

    [Test]
    public void NoPoints_AreRefused() =>
        Assert.Throws<ArgumentException>(() => MathHelper.Polyline.PointAt<double>([], 1, out _));
}
