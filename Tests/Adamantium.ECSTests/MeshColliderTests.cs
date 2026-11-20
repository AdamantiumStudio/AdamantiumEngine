using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>A mesh collider: its entity's mesh itself, fitted at its center and carried by the collider's placement.
/// </summary>
[TestFixture]
public class MeshColliderTests
{
    [Test]
    public void AFit_TakesTheMesh_AtItsCenter()
    {
        var mesh = Floor(new Vector3F(2, 0, 3), 1);
        var collider = With(new MeshCollider(), mesh);

        Assert.Multiple(() =>
        {
            Assert.That(collider.Source, Is.SameAs(mesh));
            Assert.That(collider.Position, Is.EqualTo(new Vector3F(2, 0, 3)));
            Assert.That(collider.IsFitted, Is.True);
            Assert.That(collider.Carry, Is.EqualTo(Matrix4x4F.Identity));
            Assert.That(collider.Bounds.Center.X, Is.EqualTo(2).Within(1e-6));
        });
    }

    [Test]
    public void ARayMeetsTheTriangles_WhereThePlacementCarriesThem()
    {
        var collider = With(new MeshCollider(), Floor(Vector3F.Zero, 1));
        var down = new Ray<float>(new Vector3F(0.2f, 5, 0.2f), -Vector3F.UnitY);

        var meetsFitted = collider.Intersects(ref down, out var fitted);
        collider.Position = new Vector3F(0, -2, 0);
        var meetsMoved = collider.Intersects(ref down, out var moved);
        var beside = new Ray<float>(new Vector3F(3, 5, 0), -Vector3F.UnitY);

        Assert.Multiple(() =>
        {
            Assert.That(meetsFitted, Is.True);
            Assert.That(fitted.Y, Is.EqualTo(0).Within(1e-5f));
            Assert.That(meetsMoved, Is.True);
            Assert.That(moved.Y, Is.EqualTo(-2).Within(1e-5f));
            Assert.That(collider.IsFitted, Is.False);
            Assert.That(collider.Intersects(ref beside, out _), Is.False);
        });
    }

    [Test]
    public void PlacedByHand_ItTakesANewMesh_WhereItWasPut()
    {
        var entity = new Entity(null, "Floor");
        entity.AddComponent(new MeshData { Mesh = Floor(Vector3F.Zero, 1) });
        var collider = With(new MeshCollider(), entity);
        collider.Position = new Vector3F(0, 3, 0);

        var other = Floor(new Vector3F(5, 0, 0), 2);
        collider.FollowMesh(other);

        Assert.Multiple(() =>
        {
            Assert.That(collider.Source, Is.SameAs(other));
            Assert.That(collider.Position, Is.EqualTo(new Vector3F(0, 3, 0)));
            Assert.That(collider.Bounds.Size.X, Is.EqualTo(4).Within(1e-5));
        });
    }

    [Test]
    public void TheOutline_DrawsEachEdgeOnce()
    {
        var collider = With(new MeshCollider(), Floor(Vector3F.Zero, 1));

        var outline = collider.GetVisualRepresentation();

        // Two triangles of a quad: four sides and the diagonal.
        Assert.That(outline.Points.Length, Is.EqualTo(10));
    }

    [Test]
    public void AMeshCollider_DoesNotMerge()
    {
        var collider = With(new MeshCollider(), Floor(Vector3F.Zero, 1));

        Assert.Throws<NotSupportedException>(() => collider.Merge(new BoxCollider()));
    }

    [Test]
    public void AnEntityWithoutPoints_LeavesItEmpty()
    {
        var collider = With(new MeshCollider(), new Mesh(PrimitiveType.TriangleList));
        var ray = new Ray<float>(new Vector3F(0, 5, 0), -Vector3F.UnitY);

        Assert.Multiple(() =>
        {
            Assert.That(collider.Source, Is.Null);
            Assert.That(collider.Intersects(ref ray, out _), Is.False);
            Assert.That(collider.GetWorldShape(WorldPosition.Zero), Is.Null);
        });
    }

    private static MeshCollider With(MeshCollider collider, Mesh mesh)
    {
        var entity = new Entity(null, "Shape");
        entity.AddComponent(new MeshData { Mesh = mesh });
        return With(collider, entity);
    }

    private static MeshCollider With(MeshCollider collider, Entity entity)
    {
        entity.AddComponent(collider);
        collider.Initialize();
        return collider;
    }

    // A square of side twice half at Y = 0 round center: two triangles.
    private static Mesh Floor(Vector3F center, float half)
    {
        var points = new[]
        {
            (center + new Vector3F(-half, 0, -half)).As<double>(), (center + new Vector3F(-half, 0, half)).As<double>(),
            (center + new Vector3F(half, 0, half)).As<double>(), (center + new Vector3F(half, 0, -half)).As<double>()
        };
        return new Mesh(PrimitiveType.TriangleList).SetPoints(points).SetIndices([0, 1, 2, 0, 2, 3]);
    }
}
