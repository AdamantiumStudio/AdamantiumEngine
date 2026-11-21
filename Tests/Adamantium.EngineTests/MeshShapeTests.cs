using System.Collections.Generic;
using Adamantium.ECS.Physics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A mesh collider's physics shape: the mesh's triangles carried, shared while carried alike.</summary>
[TestFixture]
public class MeshShapeTests
{
    // A ridge along Z, mirrored or not: wound back after a mirror, its triangles keep facing up, so the crest stays an
    // edge between two faces a body catches on, not a fold.
    [TestCase(1f)]
    [TestCase(-1f)]
    public void ARidgesTriangles_KeepFacingUp_WhenTheMeshIsMirrored(float mirror)
    {
        var shape = new CarriedShapeCache().Take(Ridge(), Matrix4x4.CreateScale<float>(mirror, 1, 1),
            new List<CarriedShapeKey>());

        var vertices = shape.Vertices;
        var indices = shape.Indices;
        var facings = new List<float>();
        for (var index = 0; index < indices.Count; index += 3)
        {
            var first = vertices[indices[index]];
            var normal = Vector3F.Cross(vertices[indices[index + 1]] - first, vertices[indices[index + 2]] - first);
            facings.Add(normal.Y);
        }

        Assert.That(facings, Has.Count.EqualTo(4));
        Assert.That(facings, Has.All.GreaterThan(0));
    }

    [Test]
    public void TheSameMeshCarriedAlike_SharesOneShape_AndAChangedMeshIsMadeAgain()
    {
        var cache = new CarriedShapeCache();
        var mesh = Ridge();
        var taken = new List<CarriedShapeKey>();

        var first = cache.Take(mesh, Matrix4x4F.Identity, taken);
        var second = cache.Take(mesh, Matrix4x4F.Identity, taken);
        mesh.ApplyTransform(Matrix4x4.CreateTranslation<double>(0, 1, 0));
        var changed = cache.Take(mesh, Matrix4x4F.Identity, taken);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(changed, Is.Not.SameAs(first));
        });
    }

    private static Mesh Ridge()
    {
        Vector3<double>[] points =
        [
            new(-1, 0, -1), new(-1, 0, 1), new(0, 0.5, 1), new(0, 0.5, -1), new(1, 0, 1), new(1, 0, -1)
        ];
        return new Mesh(PrimitiveType.TriangleList).SetPoints(points).SetIndices([0, 1, 2, 0, 2, 3, 3, 2, 4, 3, 4, 5]);
    }
}
