using System.Collections.Generic;
using Adamantium.Engine.Physics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A mesh collider's physics shape: the mesh's triangles carried, shared while carried alike.</summary>
[TestFixture]
public class MeshShapeTests
{
    // A ridge along Z: two slopes meeting at a crest a body catches on, mirrored or not.
    [TestCase(1f)]
    [TestCase(-1f)]
    public void ARidgesCrest_StaysAnEdgeToCatchOn_WhenTheMeshIsMirrored(float mirror)
    {
        var shape = new CarriedShapeCache().Take(Ridge(), Matrix4x4.CreateScale<float>(mirror, 1, 1),
            new List<CarriedShapeKey>());

        var active = 0;
        for (var triangle = 0; triangle < shape.TriangleCount; triangle++)
        {
            active += Count(shape.GetActiveEdges(triangle));
        }

        // Each slope's rim is three edges; the crest, seen from both slopes, two more.
        Assert.That(active, Is.EqualTo(2 * 3 + 2));
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

    private static int Count(int edges) => (edges & 1) + ((edges >> 1) & 1) + ((edges >> 2) & 1);

    private static Mesh Ridge()
    {
        Vector3<double>[] points =
        [
            new(-1, 0, -1), new(-1, 0, 1), new(0, 0.5, 1), new(0, 0.5, -1), new(1, 0, 1), new(1, 0, -1)
        ];
        return new Mesh(PrimitiveType.TriangleList).SetPoints(points).SetIndices([0, 1, 2, 0, 2, 3, 3, 2, 4, 3, 4, 5]);
    }
}
