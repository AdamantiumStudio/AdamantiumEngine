using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class ColliderShapeTests
{
    [Test]
    public void ACapsule_FitsAlongTheLongestSideOfItsMesh()
    {
        var capsule = With(new CapsuleCollider(), Box(null, new Vector3<double>(-0.5, -2, -0.5), new Vector3<double>(0.5, 2, 0.5)));

        Assert.That(capsule.Axis, Is.EqualTo(ColliderAxis.Y));
        Assert.That(capsule.Radius, Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(capsule.Height, Is.EqualTo(4f).Within(1e-5f));
        Assert.That((capsule.Capsule.Start - new Vector3F(0, -1.5f, 0)).Length(), Is.LessThan(1e-5f));
        Assert.That((capsule.Capsule.End - new Vector3F(0, 1.5f, 0)).Length(), Is.LessThan(1e-5f));
    }

    [Test]
    public void ACapsule_TakesTheDirectionItIsGiven()
    {
        var capsule = With(new CapsuleCollider(), Box(null, new Vector3<double>(-0.5, -2, -0.5), new Vector3<double>(0.5, 2, 0.5)));

        capsule.Axis = ColliderAxis.X;

        Assert.That((capsule.Capsule.End - capsule.Capsule.Start).X, Is.GreaterThan(0));
        Assert.That((capsule.Capsule.End - capsule.Capsule.Start).Y, Is.EqualTo(0f));
        Assert.That(capsule.IsFitted, Is.False, "an axis given by hand is kept");
    }

    [Test]
    public void ACapsule_IsPickedOnItsRoundedSurface()
    {
        var scene = new PickScene();
        var entity = Box(null, new Vector3<double>(-2, -0.5, -0.5), new Vector3<double>(2, 0.5, 0.5));
        entity.Transform.Position = new WorldPosition(0, 0, 10);
        With(new CapsuleCollider(), entity);
        scene.Place(entity);

        var hit = entity.Pick(scene.RayThroughCenter(), PickMode.Colliders);

        Assert.That(hit.Entity, Is.SameAs(entity));
        Assert.That(hit.Point.Z, Is.EqualTo(9.5f).Within(1e-3f));
    }

    [Test]
    public void AHull_MissesWhereTheBoxRoundItWouldHit()
    {
        var tetrahedron = Shape(null, [new Vector3<double>(0, 0, 10).As<float>(), new Vector3<double>(2, 0, 10).As<float>(), new Vector3<double>(0, 2, 10).As<float>(), new Vector3<double>(0, 0, 12).As<float>()]);
        var hull = With(new ConvexHullCollider(), tetrahedron);
        var box = new BoxCollider();
        tetrahedron.AddComponent(box);
        box.Initialize();
        var corner = new Ray<float>(new Vector3F(1.8f, 1.8f, -5), Vector3F.UnitZ);
        var middle = new Ray<float>(new Vector3F(0.3f, 0.3f, -5), Vector3F.UnitZ);

        Assert.That(box.Intersects(ref corner, out _), Is.True);
        Assert.That(hull.Intersects(ref corner, out _), Is.False);
        Assert.That(hull.Intersects(ref middle, out var point), Is.True);
        Assert.That(point.Z, Is.EqualTo(10f).Within(1e-4f));
    }

    [Test]
    public void Colliders_OverlapWhereTheirEntitiesStand()
    {
        var capsule = With(new CapsuleCollider(), Box(null, new Vector3<double>(-0.5, -2, -0.5), new Vector3<double>(0.5, 2, 0.5)));
        var hullEntity = Box(null, new Vector3<double>(-0.5, -0.5, -0.5), new Vector3<double>(0.5, 0.5, 0.5));
        var hull = With(new ConvexHullCollider(), hullEntity);

        hullEntity.Transform.Position = new WorldPosition(0.8, 1, 0);
        Assert.That(capsule.Intersects(hull), Is.True);

        hullEntity.Transform.Position = new WorldPosition(3, 1, 0);
        Assert.That(capsule.Intersects(hull), Is.False);
    }

    [Test]
    public void AFrustum_SeesACapsuleInFront_AndNotOneBehind()
    {
        var scene = new PickScene();
        var ahead = Box(null, new Vector3<double>(-0.5, -2, -0.5), new Vector3<double>(0.5, 2, 0.5));
        ahead.Transform.Position = new WorldPosition(0, 0, 10);
        var behind = Box(null, new Vector3<double>(-0.5, -2, -0.5), new Vector3<double>(0.5, 2, 0.5));
        behind.Transform.Position = new WorldPosition(0, 0, -10);
        var seen = With(new CapsuleCollider(), ahead);
        var hidden = With(new CapsuleCollider(), behind);
        scene.Place(ahead);
        scene.Place(behind);
        seen.UpdateForCamera(scene.Camera);
        hidden.UpdateForCamera(scene.Camera);

        Assert.That(seen.IsInsideCameraFrustum(scene.Camera), Is.Not.EqualTo(ContainmentType.Disjoint));
        Assert.That(hidden.IsInsideCameraFrustum(scene.Camera), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void AHull_TakesInTheHullOfAPartUnderIt()
    {
        var root = Box(null, new Vector3<double>(-1, -1, -1), new Vector3<double>(1, 1, 1));
        var rootHull = With(new ConvexHullCollider(), root);
        var part = Box(root, new Vector3<double>(-1, -1, -1), new Vector3<double>(1, 1, 1));
        part.Transform.Position = new WorldPosition(5, 0, 0);
        var partHull = With(new ConvexHullCollider(), part);

        rootHull.Merge(partHull);

        Assert.That(rootHull.Hull.Contains(new Vector3F(5.5f, 0, 0)), Is.True);
        Assert.That(rootHull.Hull.Contains(new Vector3F(3f, 0.5f, 0.5f)), Is.True);
        Assert.That(rootHull.Hull.Contains(new Vector3F(7f, 0, 0)), Is.False);
    }

    [Test]
    public void ACylinder_FitsAlongTheSideWhoseCrossSectionIsRoundest()
    {
        var wheel = With(new CylinderCollider(), Box(null, new Vector3<double>(-0.4, -0.4, -0.15), new Vector3<double>(0.4, 0.4, 0.15)));
        var barrel = With(new CylinderCollider(), Box(null, new Vector3<double>(-0.3, -0.5, -0.3), new Vector3<double>(0.3, 0.5, 0.3)));

        Assert.That(wheel.Axis, Is.EqualTo(ColliderAxis.Z));
        Assert.That(wheel.Radius, Is.EqualTo(0.4f).Within(1e-5f));
        Assert.That(wheel.Height, Is.EqualTo(0.3f).Within(1e-5f));
        Assert.That((wheel.Cylinder.End - new Vector3F(0, 0, 0.15f)).Length(), Is.LessThan(1e-5f));
        Assert.That(barrel.Axis, Is.EqualTo(ColliderAxis.Y));
        Assert.That(barrel.Height, Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void ACylinder_IsPickedOnItsSideAndItsEnd_NotBeyondItsRim()
    {
        var scene = new PickScene();
        var entity = Box(null, new Vector3<double>(-1, -0.5, -1), new Vector3<double>(1, 0.5, 1));
        entity.Transform.Position = new WorldPosition(0, 0, 10);
        var cylinder = With(new CylinderCollider(), entity);
        scene.Place(entity);

        var hit = entity.Pick(scene.RayThroughCenter(), PickMode.Colliders);
        var corner = new Ray<float>(new Vector3F(0.9f, -5, 0.9f), Vector3F.UnitY);
        var end = new Ray<float>(new Vector3F(0.2f, -5, 0.2f), Vector3F.UnitY);

        Assert.That(hit.Entity, Is.SameAs(entity));
        Assert.That(hit.Point.Z, Is.EqualTo(9f).Within(1e-3f), "on the round side, a meter from the axis");
        Assert.That(cylinder.Intersects(ref corner, out _), Is.False, "the box's corner is off the rim");
        Assert.That(cylinder.Intersects(ref end, out var point), Is.True);
        Assert.That(point.Y, Is.EqualTo(-0.5f).Within(1e-4f));
    }

    [Test]
    public void AFrustum_SeesACylinderInFront_AndNotOneBehind()
    {
        var scene = new PickScene();
        var ahead = Box(null, new Vector3<double>(-0.5, -0.5, -0.2), new Vector3<double>(0.5, 0.5, 0.2));
        ahead.Transform.Position = new WorldPosition(0, 0, 10);
        var behind = Box(null, new Vector3<double>(-0.5, -0.5, -0.2), new Vector3<double>(0.5, 0.5, 0.2));
        behind.Transform.Position = new WorldPosition(0, 0, -10);
        var seen = With(new CylinderCollider(), ahead);
        var hidden = With(new CylinderCollider(), behind);
        scene.Place(ahead);
        scene.Place(behind);
        seen.UpdateForCamera(scene.Camera);
        hidden.UpdateForCamera(scene.Camera);

        Assert.That(seen.IsInsideCameraFrustum(scene.Camera), Is.Not.EqualTo(ContainmentType.Disjoint));
        Assert.That(hidden.IsInsideCameraFrustum(scene.Camera), Is.EqualTo(ContainmentType.Disjoint));
    }

    [Test]
    public void ACylinder_OverlapsWhereItsRoundSideReaches()
    {
        var cylinder = With(new CylinderCollider(), Box(null, new Vector3<double>(-1, -0.5, -1), new Vector3<double>(1, 0.5, 1)));
        var otherEntity = Box(null, new Vector3<double>(-0.2, -0.2, -0.2), new Vector3<double>(0.2, 0.2, 0.2));
        var other = With(new BoxCollider(), otherEntity);

        otherEntity.Transform.Position = new WorldPosition(1.1, 0, 0);
        Assert.That(cylinder.Intersects(other), Is.True);

        otherEntity.Transform.Position = new WorldPosition(1, 0, 1);
        Assert.That(cylinder.Intersects(other), Is.False, "beside the round side, where a box round it would reach");
    }

    // A billion meters out two colliders a centimeter apart do not meet, and touching do: they are tested near each
    // other, not in float far out.
    [Test]
    public void FarFromTheOrigin_CollidersMeetAsNearIt()
    {
        var far = new WorldPosition(1e9, 5e8, -1e9);
        var firstEntity = Box(null, new Vector3<double>(-0.5), new Vector3<double>(0.5));
        var secondEntity = Box(null, new Vector3<double>(-0.5), new Vector3<double>(0.5));
        var first = With(new BoxCollider(), firstEntity);
        var second = With(new BoxCollider(), secondEntity);
        firstEntity.Transform.Position = far;

        secondEntity.Transform.Position = far + new Vector3<double>(1.01, 0, 0);
        var apart = first.Intersects(second);
        secondEntity.Transform.Position = far + new Vector3<double>(0.99, 0, 0);
        var touching = first.Intersects(second);

        Assert.Multiple(() =>
        {
            Assert.That(apart, Is.False, "a centimeter apart");
            Assert.That(touching, Is.True, "a centimeter into each other");
        });
    }

    [Test]
    public void AShownCollider_IsBuiltOnce_AndFollowsItsShape()
    {
        var entity = Box(null, new Vector3<double>(-1, -1, -1), new Vector3<double>(1, 1, 1));
        var box = With(new BoxCollider(), entity);
        var cylinder = With(new CylinderCollider(), Box(null, new Vector3<double>(-1, -1, -1), new Vector3<double>(1, 1, 1)));
        var beforeShown = entity.ChangeCount;
        var shownBox = box.GetVisualRepresentation();
        var shownCylinder = cylinder.GetVisualRepresentation();
        var shown = entity.ChangeCount;

        var again = box.GetVisualRepresentation();
        var shownAgain = entity.ChangeCount;
        box.Size = new Vector3F(4);
        cylinder.Radius = 3;
        var edited = box.GetVisualRepresentation();
        cylinder.GetVisualRepresentation();

        Assert.Multiple(() =>
        {
            Assert.That(shown, Is.EqualTo(beforeShown + 1), "the outline made is one change");
            Assert.That(again, Is.SameAs(shownBox));
            Assert.That(shownAgain, Is.EqualTo(shown), "shown again, nothing changed");
            Assert.That(edited, Is.SameAs(shownBox));
            Assert.That(shownBox.Bounds.Size.X, Is.EqualTo(4).Within(1e-5));
            Assert.That(shownCylinder.Bounds.Size.X, Is.EqualTo(6).Within(1e-4));
        });
    }

    [Test]
    public void ANewHull_IsToldAndShown()
    {
        var entity = Box(null, new Vector3<double>(-1, -1, -1), new Vector3<double>(1, 1, 1));
        var hull = With(new ConvexHullCollider(), entity);
        var shown = hull.GetVisualRepresentation();
        var raised = new System.Collections.Generic.List<string>();
        hull.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var before = entity.ChangeCount;

        var bigger = Box(null, new Vector3<double>(-3, -3, -3), new Vector3<double>(3, 3, 3));
        hull.FitTo(bigger.GetComponent<MeshData>().Mesh);
        hull.GetVisualRepresentation();

        Assert.Multiple(() =>
        {
            Assert.That(raised, Does.Contain(nameof(ConvexHullCollider.Hull)));
            Assert.That(entity.ChangeCount, Is.GreaterThan(before));
            Assert.That(shown.Bounds.Size.X, Is.EqualTo(6).Within(1e-4));
        });
    }

    private static T With<T>(T collider, Entity entity) where T : Collider
    {
        entity.AddComponent(collider);
        collider.Initialize();
        return collider;
    }

    private static Entity Box(Entity owner, Vector3<double> minimum, Vector3<double> maximum)
    {
        var corners = new Vector3F[OrientedBoundingBox<float>.CornerCount];
        new BoundingBox<float>(minimum.As<float>(), maximum.As<float>()).GetCorners(corners);
        return Shape(owner, corners);
    }

    private static Entity Shape(Entity owner, Vector3F[] corners)
    {
        var points = new Vector3<double>[corners.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            points[i] = corners[i].As<double>();
        }

        var entity = new Entity(owner, "Shape");
        entity.AddComponent(new MeshData { Mesh = new Mesh(PrimitiveType.TriangleList).SetPoints(points) });
        return entity;
    }
}
