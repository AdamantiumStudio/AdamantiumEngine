using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>Surfaces: triangle meshes and height fields, their edges a body can catch on, bodies resting and sliding on
/// them, rays and overlaps.</summary>
[TestFixture]
public class SurfaceTests
{
    [Test]
    public void BoxDroppedOnAMeshFloor_RestsOnItAndSleeps()
    {
        var scene = new SceneBuilder();
        AddStatic(scene.World, Floor(10, 1f, weld: true));
        var box = scene.AddBox(new Vector3F(0.3f, 2, -0.2f), new Vector3F(0.5f));

        scene.Run(3);

        Assert.Multiple(() =>
        {
            Assert.That(scene.World.GetPose(box).Position.Y, Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(scene.World.IsAwake(box), Is.False);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void BoxSlidingOverAMeshFloor_DoesNotCatchOnItsSeams(bool weld)
    {
        var scene = new SceneBuilder();
        AddStatic(scene.World, Floor(20, 0.5f, weld), friction: 0);
        var box = scene.World.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(new Vector3F(0.25f)),
            Position = new Vector3<double>(-3, 0.25f, 0.1f),
            LinearVelocity = new Vector3F(4, 0, 0),
            Friction = 0
        });

        var highest = 0f;
        var lowest = float.MaxValue;
        for (var step = 0; step < 60; step++)
        {
            scene.World.Step();
            var height = (float)scene.World.GetPose(box).Position.Y;
            highest = MathF.Max(highest, height);
            lowest = MathF.Min(lowest, height);
        }

        Assert.Multiple(() =>
        {
            Assert.That(highest, Is.LessThan(0.26f), "no hop over a seam");
            Assert.That(lowest, Is.GreaterThan(0.24f));
            Assert.That(scene.World.GetLinearVelocity(box).X, Is.EqualTo(4).Within(0.05f), "no speed lost on a seam");
        });
    }

    [Test]
    public void SphereRollingAcrossAHeightField_StaysOnIt()
    {
        var scene = new SceneBuilder();
        AddStatic(scene.World, FlatField(30, 0.5f, 0), friction: 0);
        var sphere = scene.World.Add(new BodyDescription
        {
            Shape = new SphereCollisionShape(0.3f),
            Position = new Vector3<double>(-4, 0.3f, 0.05f),
            LinearVelocity = new Vector3F(3, 0, 0),
            Friction = 0
        });

        scene.Run(1);

        var pose = scene.World.GetPose(sphere);
        Assert.Multiple(() =>
        {
            Assert.That(pose.Position.Y, Is.EqualTo(0.3f).Within(0.005f));
            Assert.That(pose.Position.X, Is.EqualTo(-1).Within(0.05f));
        });
    }

    [Test]
    public void BoxDroppedInAValley_RestsOnBothSlopes()
    {
        // Two triangles meeting along Z at X = 0, each rising 45 degrees away from the seam.
        var vertices = new[]
        {
            new Vector3F(-2, 2, -2), new Vector3F(-2, 2, 2), new Vector3F(0, 0, 2), new Vector3F(0, 0, -2),
            new Vector3F(2, 2, 2), new Vector3F(2, 2, -2)
        };
        int[] indices = [0, 1, 2, 0, 2, 3, 3, 2, 4, 3, 4, 5];
        var scene = new SceneBuilder();
        AddStatic(scene.World, new TriangleMeshCollisionShape(vertices, indices));
        var box = scene.AddBox(new Vector3F(0, 2, 0), new Vector3F(0.5f),
            rotation: Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 4));

        scene.Run(3);

        var pose = scene.World.GetPose(box);
        Assert.Multiple(() =>
        {
            Assert.That(pose.Position.X, Is.EqualTo(0).Within(0.01f));
            Assert.That(pose.Position.Y, Is.EqualTo(MathF.Sqrt(0.5f)).Within(0.02f), "its corner in the seam");
            Assert.That(scene.World.IsAwake(box), Is.False);
        });
    }

    [Test]
    public void SphereOverAHoleInAHeightField_FallsThrough()
    {
        var heights = new float[11 * 11];
        heights[5 * 11 + 5] = float.NaN;
        var scene = new SceneBuilder(new PhysicsSettings());
        AddStatic(scene.World, new HeightFieldCollisionShape(11, 11, heights, 1, 1));
        var sphere = scene.AddSphere(new Vector3F(0, 1, 0), 0.4f);

        scene.Run(1.5f);

        Assert.That(scene.World.GetPose(sphere).Position.Y, Is.LessThan(-2));
    }

    [Test]
    public void EdgesInsideAFlatMesh_AreNotOnesToCatchOn_ButItsRimAndRidgesAre()
    {
        var flat = Floor(2, 1, weld: false);
        var ridge = new TriangleMeshCollisionShape(
            [new Vector3F(-1, 0, -1), new Vector3F(-1, 0, 1), new Vector3F(0, 0.5f, 1), new Vector3F(0, 0.5f, -1),
             new Vector3F(1, 0, 1), new Vector3F(1, 0, -1)],
            [0, 1, 2, 0, 2, 3, 3, 2, 4, 3, 4, 5]);

        var innerCount = 0;
        var rimCount = 0;
        for (var triangle = 0; triangle < flat.TriangleCount; triangle++)
        {
            for (var edge = 0; edge < 3; edge++)
            {
                if ((flat.GetActiveEdges(triangle) & (1 << edge)) != 0)
                    rimCount++;
                else
                    innerCount++;
            }
        }

        Assert.Multiple(() =>
        {
            // Two by two quads: eight rim edges, eight inner (four diagonals, four between quads, each seen twice).
            Assert.That(rimCount, Is.EqualTo(8));
            Assert.That(innerCount, Is.EqualTo(16));
            // The second triangle of the ridge meets the third along its crest, its second edge.
            Assert.That(ridge.GetActiveEdges(1) & 2, Is.Not.Zero, "the crest is convex and sharp");
            Assert.That(ridge.GetActiveEdges(0) & 4, Is.Zero, "the diagonal within a slope is flat");
        });
    }

    [Test]
    public void ValleyEdge_IsNotOneToCatchOn()
    {
        var valley = new TriangleMeshCollisionShape(
            [new Vector3F(-1, 1, -1), new Vector3F(-1, 1, 1), new Vector3F(0, 0, 1), new Vector3F(0, 0, -1),
             new Vector3F(1, 1, 1), new Vector3F(1, 1, -1)],
            [0, 1, 2, 0, 2, 3, 3, 2, 4, 3, 4, 5]);

        Assert.That(valley.GetActiveEdges(1) & 2, Is.Zero);
    }

    [Test]
    public void DynamicBodyWithASurface_IsRefused()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var floor = Floor(2, 1, weld: true);

        Assert.Throws<ArgumentException>(() => world.Add(new BodyDescription { Shape = floor }));
        Assert.Throws<ArgumentException>(() => world.Add(new BodyDescription
        {
            Shape = new CompoundCollisionShape([new CompoundPart(floor, Pose.Identity)])
        }));

        var body = AddStatic(world, floor);
        Assert.Throws<ArgumentException>(() => world.SetKind(body, BodyKind.Dynamic));
        Assert.That(world.GetKind(body), Is.EqualTo(BodyKind.Static));
        var box = world.Add(new BodyDescription { Shape = new BoxCollisionShape(new Vector3F(0.5f)), Position = new Vector3<double>(0, 5, 0) });
        Assert.Throws<ArgumentException>(() => world.SetShape(box, floor));
    }

    [Test]
    public void RayDown_MeetsTheMeshAndTheHeightFieldWhereTheyStand()
    {
        IPhysicsWorld world = new PhysicsWorld();
        AddStatic(world, Floor(4, 1, weld: true), new Vector3F(-10, 0, 0));
        var slope = new float[5 * 5];
        for (var row = 0; row < 5; row++)
        {
            for (var column = 0; column < 5; column++)
            {
                slope[row * 5 + column] = column * 0.5f;
            }
        }

        AddStatic(world, new HeightFieldCollisionShape(5, 5, slope, 1, 1), new Vector3F(10, 0, 0));

        var meetsMesh = world.RayCast(new Vector3<double>(-10.3f, 5, 0.2f), -Vector3F.UnitY, 10, out var meshHit);
        var meetsField = world.RayCast(new Vector3<double>(10.5f, 5, 0.3f), -Vector3F.UnitY, 10, out var fieldHit);
        var slanted = world.RayCast(new Vector3<double>(7, 5, 0.2f), Vector3F.Normalize(new Vector3F(1, -1, 0.1f)), 20,
            out var slantedHit);

        Assert.Multiple(() =>
        {
            Assert.That(meetsMesh, Is.True);
            Assert.That(meshHit.Distance, Is.EqualTo(5).Within(1e-4f));
            Assert.That(meshHit.Normal.Y, Is.EqualTo(1).Within(1e-4f));
            Assert.That(meetsField, Is.True);
            // Column 2.5 of the field stands 1.25 high.
            Assert.That(fieldHit.Distance, Is.EqualTo(5 - 1.25f).Within(1e-4f));
            Assert.That(slanted, Is.True);
            Assert.That(slantedHit.Position.Y, Is.EqualTo((slantedHit.Position.X - 8) * 0.5f).Within(1e-3f),
                "on the slope");
        });
    }

    [Test]
    public void RayIntoAHole_PassesThrough_AsFarAsItMayGo()
    {
        var heights = new float[3 * 3];
        heights[4] = float.NaN;
        var field = new HeightFieldCollisionShape(3, 3, heights, 1, 1);
        var down = new Ray<float>(new Vector3F(0.1f, 1, 0.1f), -Vector3F.UnitY);
        var up = new Ray<float>(new Vector3F(0.1f, -1, 0.1f), Vector3F.UnitY);

        Assert.Multiple(() =>
        {
            Assert.That(field.CastRay(down, 5, out _, out _), Is.False);
            Assert.That(field.CastRay(down, float.PositiveInfinity, out _, out _), Is.False);
            Assert.That(field.CastRay(up, float.PositiveInfinity, out _, out _), Is.False);
        });
    }

    [Test]
    public void BoxSlidingOverADoubleSidedFloor_DoesNotCatchOnItsSeams()
    {
        var front = Floor(20, 0.5f, weld: true);
        var indices = new List<int>();
        for (var triangle = 0; triangle < front.TriangleCount; triangle++)
        {
            front.TryGetTriangle(triangle, out var first, out var second, out var third);
            var at = IndexOf(front, first);
            var next = IndexOf(front, second);
            var last = IndexOf(front, third);
            indices.AddRange([at, next, last, at, last, next]);
        }

        var doubled = new TriangleMeshCollisionShape([.. front.Vertices], indices);
        var active = 0;
        for (var triangle = 0; triangle < doubled.TriangleCount; triangle++)
        {
            active |= doubled.GetActiveEdges(triangle) == 7 ? 1 : 0;
        }

        Assert.Multiple(() =>
        {
            Assert.That(doubled.TriangleCount, Is.EqualTo(front.TriangleCount), "the back is the front once more");
            Assert.That(active, Is.Zero, "no triangle of a wide floor has every edge on the rim");
        });
    }

    private static int IndexOf(TriangleMeshCollisionShape mesh, Vector3F corner)
    {
        for (var index = 0; index < mesh.Vertices.Count; index++)
        {
            if (mesh.Vertices[index] == corner)
                return index;
        }

        return -1;
    }

    [Test]
    public void BoxOverlappingTheFloor_IsFound_AndOneAboveItIsNot()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var floor = AddStatic(world, Floor(4, 1, weld: true));
        var found = new List<BodyHandle>();

        world.Overlap(new BoxCollisionShape(new Vector3F(0.2f)), new Pose(new Vector3<double>(0.5f, 0.1f, 0.5f)), found);
        var touching = found.Count;
        found.Clear();
        world.Overlap(new BoxCollisionShape(new Vector3F(0.2f)), new Pose(new Vector3<double>(0.5f, 0.5f, 0.5f)), found);

        Assert.Multiple(() =>
        {
            Assert.That(touching, Is.EqualTo(1));
            Assert.That(found, Is.Empty);
        });
        Assert.Throws<ArgumentException>(() => world.Overlap(Floor(1, 1, weld: true), Pose.Identity, found));
        Assert.That(world.Contains(floor), Is.True);
    }

    [Test]
    public void FloorTakenAway_LetsGoOfTheContactsOfItsTriangles()
    {
        var scene = new SceneBuilder();
        var floor = AddStatic(scene.World, Floor(4, 1, weld: true));
        var box = scene.AddBox(new Vector3F(0.5f, 0.5f, 0.5f), new Vector3F(0.5f));
        scene.Run(0.5f);
        var touching = scene.World.LastStep.TouchingPairs;

        scene.World.Remove(floor);
        scene.World.Step();

        Assert.Multiple(() =>
        {
            Assert.That(touching, Is.GreaterThan(0));
            Assert.That(scene.World.LastStep.TouchingPairs, Is.Zero);
            Assert.That(scene.World.IsAwake(box), Is.True);
        });
    }

    [Test]
    public void MeshWithNoAreaOrBadIndices_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => _ = new TriangleMeshCollisionShape(
            [Vector3F.Zero, Vector3F.UnitX, new Vector3F(2, 0, 0)], [0, 1, 2]));
        Assert.Throws<ArgumentException>(() => _ = new TriangleMeshCollisionShape([Vector3F.Zero], [0, 0]));
        Assert.Throws<ArgumentException>(() => _ = new TriangleMeshCollisionShape(
            [Vector3F.Zero, Vector3F.UnitX, Vector3F.UnitZ], [0, 1, 3]));
        Assert.Throws<ArgumentException>(() => _ = new TriangleMeshCollisionShape(
            [Vector3F.Zero, Vector3F.UnitX, new Vector3F(float.NaN)], [0, 1, 2]));
        Assert.That(new TriangleMeshCollisionShape([Vector3F.Zero, Vector3F.UnitX, Vector3F.UnitZ, new Vector3F(float.NaN)],
            [0, 1, 2]).BoundingRadius, Is.EqualTo(1), "a stray vertex no triangle stands on is left out");
        Assert.Throws<ArgumentException>(() => _ = new HeightFieldCollisionShape(2, 2, [float.NaN, 0, 0, 0], 1, 1));
        Assert.Throws<ArgumentException>(() => _ = new HeightFieldCollisionShape(2, 2, [0, 0, 0], 1, 1));
    }

    private static BodyHandle AddStatic(IPhysicsWorld world, CollisionShape shape, Vector3F position = default,
        float friction = 0.6f) =>
        world.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = shape, Position = position.As<double>(), Friction = friction
        });

    // A square floor at Y = 0 of count by count quads of side size, centered; welded or each triangle with corners
    // of its own, as a mesh with its normals split stores them.
    private static TriangleMeshCollisionShape Floor(int count, float size, bool weld)
    {
        var vertices = new List<Vector3F>();
        var indices = new List<int>();
        var half = count * size / 2;
        for (var row = 0; row < count; row++)
        {
            for (var column = 0; column < count; column++)
            {
                var corner00 = new Vector3F(column * size - half, 0, row * size - half);
                var corner10 = corner00 + new Vector3F(size, 0, 0);
                var corner01 = corner00 + new Vector3F(0, 0, size);
                var corner11 = corner00 + new Vector3F(size, 0, size);
                AddTriangle(vertices, indices, corner00, corner01, corner11, weld);
                AddTriangle(vertices, indices, corner00, corner11, corner10, weld);
            }
        }

        return new TriangleMeshCollisionShape(vertices, indices);
    }

    private static void AddTriangle(List<Vector3F> vertices, List<int> indices, Vector3F first, Vector3F second,
        Vector3F third, bool weld)
    {
        foreach (var corner in new[] { first, second, third })
        {
            var at = weld ? vertices.IndexOf(corner) : -1;
            if (at < 0)
            {
                at = vertices.Count;
                vertices.Add(corner);
            }

            indices.Add(at);
        }
    }

    private static HeightFieldCollisionShape FlatField(int count, float spacing, float height)
    {
        var heights = new float[count * count];
        Array.Fill(heights, height);
        return new HeightFieldCollisionShape(count, count, heights, spacing, spacing);
    }
}
