using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

[TestFixture]
public class PickingTests
{
    [Test]
    public void Ray_LooksIntoTheView()
    {
        var ray = new PickScene().RayThroughCenter().Ray;

        Assert.That(Vector3F.Dot(ray.Direction, Vector3F.UnitZ), Is.GreaterThan(0.9999f));
        Assert.That(ray.Position.Z, Is.EqualTo(0.1f).Within(1e-3f));
    }

    [Test]
    public void MeshColliders_HitThePart_CollidersTheModel()
    {
        var scene = new PickScene();
        var model = new Entity(null, "Model");
        var part = PickScene.Box(model, "Part", new Vector3(0, 0, 10), 1);
        PickScene.Box(model, "Other", new Vector3(-4, 0, 10), 1);
        PickScene.Bounds(model, new Vector3F(-5, -1, 8), new Vector3F(1, 1, 12));
        scene.Place(model);
        var ray = scene.RayThroughCenter();

        Assert.That(model.Pick(ray, PickMode.MeshColliders).Entity, Is.SameAs(part));
        Assert.That(model.Pick(ray, PickMode.Colliders).Entity, Is.SameAs(model));
    }

    [Test]
    public void MeshColliders_MissBetweenParts()
    {
        var scene = new PickScene();
        var model = new Entity(null, "Model");
        PickScene.Box(model, "Left", new Vector3(-2, 0, 10), 1);
        PickScene.Box(model, "Right", new Vector3(2, 0, 10), 1);
        PickScene.Bounds(model, new Vector3F(-3, -1, 9), new Vector3F(3, 1, 11));
        scene.Place(model);
        var ray = scene.RayThroughCenter();

        Assert.That(model.Pick(ray, PickMode.MeshColliders).IsHit, Is.False);
        Assert.That(model.Pick(ray, PickMode.Colliders).Entity, Is.SameAs(model));
    }

    [Test]
    public void Triangles_HitAMeshOfOneTriangle()
    {
        var scene = new PickScene();
        var triangle = PickScene.Shape(null, "Triangle", PrimitiveType.TriangleList,
            [new Vector3(-1, -1, 10), new Vector3(1, -1, 10), new Vector3(0, 1, 10)], [0, 1, 2]);
        scene.Place(triangle);

        var hit = triangle.Pick(scene.RayThroughCenter(), PickMode.Triangles);

        Assert.That(hit.Entity, Is.SameAs(triangle));
        Assert.That(hit.Point.Z, Is.EqualTo(10f).Within(1e-3f));
    }

    [Test]
    public void Triangles_HitAMeshWithoutIndices()
    {
        var scene = new PickScene();
        var triangle = PickScene.Shape(null, "Triangle", PrimitiveType.TriangleList,
            [new Vector3(-1, -1, 10), new Vector3(1, -1, 10), new Vector3(0, 1, 10)]);
        scene.Place(triangle);

        Assert.That(triangle.Pick(scene.RayThroughCenter(), PickMode.Triangles).Entity, Is.SameAs(triangle));
    }

    [Test]
    public void Triangles_TakeTheNearestInsideOneMesh()
    {
        var scene = new PickScene();
        var mesh = PickScene.Shape(null, "Two", PrimitiveType.TriangleList,
        [
            new Vector3(-1, -1, 20), new Vector3(1, -1, 20), new Vector3(0, 1, 20),
            new Vector3(-1, -1, 10), new Vector3(1, -1, 10), new Vector3(0, 1, 10)
        ], [0, 1, 2, 3, 4, 5]);
        scene.Place(mesh);

        Assert.That(mesh.Pick(scene.RayThroughCenter(), PickMode.Triangles).Point.Z, Is.EqualTo(10f).Within(1e-3f));
    }

    [Test]
    public void Triangles_PickThePartUnderThePointer_NotThePartWhoseBoxHoldsIt()
    {
        var scene = new PickScene();
        var model = new Entity(null, "Model");
        PickScene.Shape(model, "Frame", PrimitiveType.TriangleList,
        [
            new Vector3(-5, -1, 10), new Vector3(-3, -1, 10), new Vector3(-4, 1, 10),
            new Vector3(3, -1, 10), new Vector3(5, -1, 10), new Vector3(4, 1, 10)
        ], [0, 1, 2, 3, 4, 5]);
        var part = PickScene.Shape(model, "Part", PrimitiveType.TriangleList,
            [new Vector3(-1, -1, 15), new Vector3(1, -1, 15), new Vector3(0, 1, 15)], [0, 1, 2]);
        scene.Place(model);

        Assert.That(model.Pick(scene.RayThroughCenter(), PickMode.Triangles).Entity, Is.SameAs(part));
    }

    [Test]
    public void Triangles_FollowTheMeshWhenItMoves()
    {
        var scene = new PickScene();
        var triangle = PickScene.Shape(null, "Triangle", PrimitiveType.TriangleList,
            [new Vector3(-1, -1, 10), new Vector3(1, -1, 10), new Vector3(0, 1, 10)], [0, 1, 2]);
        scene.Place(triangle);
        Assert.That(triangle.Pick(scene.RayThroughCenter(), PickMode.Triangles).Point.Z, Is.EqualTo(10f).Within(1e-3f));

        triangle.GetComponent<MeshData>().Mesh.ApplyTransform(Matrix4x4.Translation(0, 0, 5));

        Assert.That(triangle.Pick(scene.RayThroughCenter(), PickMode.Triangles).Point.Z, Is.EqualTo(15f).Within(1e-3f));
    }

    [Test]
    public void Triangles_FollowAStrip()
    {
        var scene = new PickScene();
        var quad = PickScene.Shape(null, "Quad", PrimitiveType.TriangleStrip,
            [new Vector3(-1, -1, 10), new Vector3(-1, 1, 10), new Vector3(1, -1, 10), new Vector3(1, 1, 10)]);
        scene.Place(quad);

        Assert.That(quad.Pick(scene.RayThroughCenter(), PickMode.Triangles).Entity, Is.SameAs(quad));
    }

    [Test]
    public void Lines_ReadAnIndexedListAsPairs()
    {
        var scene = new PickScene();
        var lines = PickScene.Shape(null, "Pairs", PrimitiveType.LineList,
        [
            new Vector3(-5, -4, 10), new Vector3(-3, -4, 10),
            new Vector3(3, 4, 10), new Vector3(5, 4, 10)
        ], [0, 1, 2, 3]);
        scene.Place(lines);

        Assert.That(lines.Pick(scene.RayThroughCenter(), PickMode.Lines, 3).IsHit, Is.False);
    }

    [TestCase(10f)]
    [TestCase(100f)]
    public void Lines_ApertureIsInPixels(float depth)
    {
        var scene = new PickScene();
        var x = 3 * scene.PixelWidthAt(depth);
        var line = PickScene.Shape(null, "Line", PrimitiveType.LineList,
            [new Vector3(x, -5, depth), new Vector3(x, 5, depth)]);
        scene.Place(line);
        var ray = scene.RayThroughCenter();

        Assert.That(line.Pick(ray, PickMode.Lines, 4).IsHit, Is.True);
        Assert.That(line.Pick(ray, PickMode.Lines, 2).IsHit, Is.False);
    }

    [Test]
    public void Disabled_IsSkippedWithWhatIsUnderIt()
    {
        var scene = new PickScene();
        var parent = PickScene.Box(null, "Parent", new Vector3(0, 0, 10), 1);
        PickScene.Box(parent, "Child", new Vector3(0, 0, 10), 1);
        parent.IsEnabled = false;
        scene.Place(parent);

        Assert.That(parent.Pick(scene.RayThroughCenter(), PickMode.Colliders).IsHit, Is.False);
    }

    [Test]
    public void Disabled_IsPickedWithWhatIsUnderIt_WhenAskedFor_ButHiddenNever()
    {
        var scene = new PickScene();
        var parent = PickScene.Box(null, "Parent", new Vector3(0, 0, 10), 1);
        var child = PickScene.Box(parent, "Child", new Vector3(0, 0, -5), 1);
        parent.IsEnabled = false;
        scene.Place(parent);
        var ray = scene.RayThroughCenter();

        Assert.That(parent.Pick(ray, PickMode.Colliders, withDisabled: true).Entity, Is.SameAs(child));

        parent.Visible = false;

        Assert.That(parent.Pick(ray, PickMode.Colliders, withDisabled: true).IsHit, Is.False);
    }

    [Test]
    public void Hidden_IsSkippedWithWhatIsUnderIt_AndLetsTheRayThrough()
    {
        var scene = new PickScene();
        var hidden = PickScene.Box(null, "Hidden", new Vector3(0, 0, 10), 1);
        PickScene.Box(hidden, "Child", new Vector3(0, 0, -5), 1);
        var behind = PickScene.Box(null, "Behind", new Vector3(0, 0, 20), 1);
        hidden.Visible = false;
        scene.Place(hidden);
        scene.Place(behind);
        var ray = scene.RayThroughCenter();

        Assert.Multiple(() =>
        {
            Assert.That(hidden.Pick(ray, PickMode.Colliders).IsHit, Is.False, "the child, nearest of all, is hidden with its root");
            Assert.That(Picking.Pick([hidden, behind], ray, PickMode.Colliders).Entity, Is.SameAs(behind));
        });
    }

    [Test]
    public void Ignored_LeavesWhatIsUnderItPickable()
    {
        var scene = new PickScene();
        var parent = PickScene.Box(null, "Parent", new Vector3(0, 0, 10), 1);
        var child = PickScene.Box(parent, "Child", new Vector3(0, 0, 10), 1);
        parent.IgnoreInCollisionDetection = true;
        scene.Place(parent);

        Assert.That(parent.Pick(scene.RayThroughCenter(), PickMode.Colliders).Entity, Is.SameAs(child));
    }

    [Test]
    public void Roots_NearestWinsWhateverTheOrder()
    {
        var scene = new PickScene();
        var far = PickScene.Box(null, "Far", new Vector3(0, 0, 20), 1);
        var near = PickScene.Box(null, "Near", new Vector3(0, 0, 10), 1);
        scene.Place(far);
        scene.Place(near);

        Assert.That(Picking.Pick([far, near], scene.RayThroughCenter(), PickMode.Colliders).Entity, Is.SameAs(near));
    }

    [Test]
    public void Scaled_HitsItsScaledSurface()
    {
        var scene = new PickScene();
        var ball = PickScene.Ball(null, "Ball", new Vector3(0, 0, 20), 1);
        ball.Transform.SetScaleFactor(3);
        scene.Place(ball);

        var hit = ball.Pick(scene.RayThroughCenter(), PickMode.Colliders);

        Assert.That(hit.Point.Z, Is.EqualTo(17f).Within(1e-2f));
        Assert.That(hit.Depth, Is.EqualTo(17f - 0.1f).Within(1e-2f));
    }

    [Test]
    public void CollidersWithMeshColliders_Throws()
    {
        var scene = new PickScene();
        var box = PickScene.Box(null, "Box", new Vector3(0, 0, 10), 1);
        scene.Place(box);

        Assert.Throws<ArgumentException>(() => box.Pick(scene.RayThroughCenter(), PickMode.Colliders | PickMode.MeshColliders));
    }
}
