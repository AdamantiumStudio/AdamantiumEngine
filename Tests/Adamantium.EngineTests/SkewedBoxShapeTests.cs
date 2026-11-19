using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using Adamantium.ECS.Components;
using Adamantium.Engine.Physics;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A box collider turned on an unevenly scaled entity: the physics takes the hull of its corners.</summary>
[TestFixture]
public class SkewedBoxShapeTests
{
    // A unit box turned 30° about Z, then stretched twice along X: its corner (0.5, -0.5) is turned to (0.683, -0.183)
    // and stretched to (1.366, -0.183). Stretched first or turned the other way, no corner stands there.
    [TestCase(2f)]
    [TestCase(-2f)]
    public void ABoxTurnedThenStretched_IsTheHullOfItsCorners(float stretch)
    {
        var shape = Make(new Vector3F(1), 30, new Vector3F(stretch, 1, 1), new HullShapeCache());
        var hull = (ConvexHullCollisionShape)shape.Shape;
        var turned = MathF.Cos(MathF.PI / 6) / 2;
        var corner = new Vector3F((turned + 0.25f) * stretch, 0.25f - turned, 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(HasCorner(hull, corner), Is.True);
            Assert.That(HasCorner(hull, new Vector3F(corner.X, -corner.Y, corner.Z)), Is.False);
            Assert.That(shape.Offset, Is.EqualTo(new Vector3F(stretch, 0, 0)));
            Assert.That(shape.Turn, Is.EqualTo(QuaternionF.Identity));
            Assert.That(hull.ConvexRadius, Is.Zero);
        });
    }

    // A plate with no thickness, made a thousandth of its side thick: 0.5 cos 30° + 0.0005 sin 30° wide, stretched by 10.
    [Test]
    public void AFlatBoxTurnedThenStretched_IsAThinSolid()
    {
        var shape = Make(new Vector3F(1, 0, 1), 30, new Vector3F(10, 1, 5), new HullShapeCache());
        var reach = Reach((ConvexHullCollisionShape)shape.Shape);

        Assert.Multiple(() =>
        {
            Assert.That(reach.X, Is.EqualTo((0.5f * MathF.Cos(MathF.PI / 6) + 0.00025f) * 10).Within(1e-4));
            Assert.That(reach.Z, Is.EqualTo(2.5f).Within(1e-4));
        });
    }

    // A thin wall, a two-hundredth of its length, is left as thin as it is: 1 cos 60° + 0.005 sin 60° wide.
    [Test]
    public void AThinWallTurnedThenStretched_KeepsItsThickness()
    {
        var shape = Make(new Vector3F(2, 0.01f, 1), 60, new Vector3F(1, 3, 1), new HullShapeCache());
        var reach = Reach((ConvexHullCollisionShape)shape.Shape);

        Assert.That(reach.X, Is.EqualTo(0.5f + 0.005f * MathF.Sin(MathF.PI / 3)).Within(1e-5));
    }

    [Test]
    public void TheSameSkewedBox_SharesOneShape()
    {
        var hullShapes = new HullShapeCache();
        var first = Make(new Vector3F(1), 30, new Vector3F(2, 1, 1), hullShapes);
        var second = Make(new Vector3F(1), 30, new Vector3F(2, 1, 1), hullShapes);
        var other = Make(new Vector3F(1), 40, new Vector3F(2, 1, 1), hullShapes);

        Assert.Multiple(() =>
        {
            Assert.That(second.Shape, Is.SameAs(first.Shape));
            Assert.That(other.Shape, Is.Not.SameAs(first.Shape));
        });
    }

    private static ColliderShape Make(Vector3F size, float degrees, Vector3F scale, HullShapeCache hullShapes)
    {
        var box = new BoxCollider
        {
            Size = size,
            Position = new Vector3F(1, 0, 0),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, float.DegreesToRadians(degrees))
        };
        Assert.That(ColliderShape.TryCreate(box, scale, hullShapes, new List<HullShapeKey>(), out var shape), Is.True);
        return shape;
    }

    private static bool HasCorner(ConvexHullCollisionShape hull, Vector3F wanted)
    {
        foreach (var corner in hull.Vertices)
        {
            if (Vector3F.Distance(corner, wanted) < 1e-4f)
                return true;
        }

        return false;
    }

    private static Vector3F Reach(ConvexHullCollisionShape hull)
    {
        var reach = Vector3F.Zero;
        foreach (var corner in hull.Vertices)
        {
            reach = Vector3F.Max(reach, Vector3F.Abs(corner));
        }

        return reach;
    }
}
