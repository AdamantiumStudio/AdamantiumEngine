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

        Assert.That(capsule.Direction, Is.EqualTo(CapsuleDirection.Y));
        Assert.That(capsule.Radius, Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(capsule.Height, Is.EqualTo(4f).Within(1e-5f));
        Assert.That((capsule.Capsule.Start - new Vector3F(0, -1.5f, 0)).Length(), Is.LessThan(1e-5f));
        Assert.That((capsule.Capsule.End - new Vector3F(0, 1.5f, 0)).Length(), Is.LessThan(1e-5f));
    }

    [Test]
    public void ACapsule_TakesTheDirectionItIsGiven()
    {
        var capsule = With(new CapsuleCollider(), Box(null, new Vector3<double>(-0.5, -2, -0.5), new Vector3<double>(0.5, 2, 0.5)));

        capsule.Direction = CapsuleDirection.X;

        Assert.That((capsule.Capsule.End - capsule.Capsule.Start).X, Is.GreaterThan(0));
        Assert.That((capsule.Capsule.End - capsule.Capsule.Start).Y, Is.EqualTo(0f));
    }

    [Test]
    public void ACapsule_IsPickedOnItsRoundedSurface()
    {
        var scene = new PickScene();
        var entity = Box(null, new Vector3<double>(-2, -0.5, -0.5), new Vector3<double>(2, 0.5, 0.5));
        entity.Transform.Position = new Vector3<double>(0, 0, 10);
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

        hullEntity.Transform.Position = new Vector3<double>(0.8, 1, 0);
        Assert.That(capsule.Intersects(hull), Is.True);

        hullEntity.Transform.Position = new Vector3<double>(3, 1, 0);
        Assert.That(capsule.Intersects(hull), Is.False);
    }

    [Test]
    public void AFrustum_SeesACapsuleInFront_AndNotOneBehind()
    {
        var scene = new PickScene();
        var ahead = Box(null, new Vector3<double>(-0.5, -2, -0.5), new Vector3<double>(0.5, 2, 0.5));
        ahead.Transform.Position = new Vector3<double>(0, 0, 10);
        var behind = Box(null, new Vector3<double>(-0.5, -2, -0.5), new Vector3<double>(0.5, 2, 0.5));
        behind.Transform.Position = new Vector3<double>(0, 0, -10);
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
        part.Transform.Position = new Vector3<double>(5, 0, 0);
        var partHull = With(new ConvexHullCollider(), part);

        rootHull.Merge(partHull);

        Assert.That(rootHull.Hull.Contains(new Vector3F(5.5f, 0, 0)), Is.True);
        Assert.That(rootHull.Hull.Contains(new Vector3F(3f, 0.5f, 0.5f)), Is.True);
        Assert.That(rootHull.Hull.Contains(new Vector3F(7f, 0, 0)), Is.False);
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
