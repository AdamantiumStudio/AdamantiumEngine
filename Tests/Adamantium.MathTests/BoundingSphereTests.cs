using System;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

[TestFixture]
public class BoundingSphereTests
{
    private static readonly OrientedBoundingBox<float> Turned =
        new(Vector3F.Zero, Vector3F.One, Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitZ, (MathF.PI / 4)));

    [Test]
    public void ASphereInATurnedBoxCorner_Intersects()
    {
        var box = Turned;
        var sphere = new BoundingSphere<float>(new Vector3F(1.3f, 0, 0), 0.05f);

        Assert.That(sphere.Intersects(box), Is.True);
        Assert.That(box.Intersects(sphere), Is.True);
    }

    [Test]
    public void ASphereOffATurnedBoxEdge_IsDisjoint()
    {
        var box = Turned;
        var sphere = new BoundingSphere<float>(new Vector3F(1.2f, 1.2f, 0), 0.2f);

        Assert.That(sphere.Intersects(box), Is.False);
        Assert.That(box.Intersects(sphere), Is.False);
    }

    [Test]
    public void ASphereRoundATurnedBox_ContainsIt()
    {
        var box = Turned;
        var sphere = new BoundingSphere<float>(Vector3F.Zero, 1.8f);

        Assert.That(sphere.Contains(box), Is.EqualTo(ContainmentType.Contains));
    }
}
