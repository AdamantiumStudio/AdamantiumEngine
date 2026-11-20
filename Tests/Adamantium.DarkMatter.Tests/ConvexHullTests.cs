using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.CollisionDetection;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>Convex hulls: their mass and faces, their contacts with every shape by GJK, EPA and clipped faces, and
/// hulls at rest in a world.</summary>
[TestFixture]
public class ConvexHullTests
{
    private const float Speculative = 0.02f;
    private readonly ManifoldBuffer _manifold = new();

    [Test]
    public void HullOfABoxsCorners_WeighsAndTurnsAsTheBox()
    {
        var hull = Cube(new Vector3F(1, 0.5f, 0.25f));
        var box = new BoxCollisionShape(1, 0.5f, 0.25f).ComputeMass(1000);

        var mass = hull.ComputeMass(1000);

        Assert.Multiple(() =>
        {
            Assert.That(hull.FaceCount, Is.EqualTo(6), "two triangles a side make one face");
            Assert.That(mass.Mass, Is.EqualTo(box.Mass).Within(box.Mass * 1e-5f));
            Assert.That(mass.Center.Length(), Is.LessThan(1e-5f));
            var moments = new[] { mass.Inertia.X, mass.Inertia.Y, mass.Inertia.Z };
            Array.Sort(moments);
            var expected = new[] { box.Inertia.X, box.Inertia.Y, box.Inertia.Z };
            Array.Sort(expected);
            for (var index = 0; index < 3; index++)
            {
                Assert.That(moments[index], Is.EqualTo(expected[index]).Within(expected[index] * 1e-4f));
            }
        });
    }

    [Test]
    public void Tetrahedron_HasItsCenterOfMassAtTheMeanOfItsCorners()
    {
        var corners = new[] { new Vector3F(0, 0, 0), new Vector3F(3, 0, 0), new Vector3F(0, 3, 0), new Vector3F(0, 0, 3) };
        var hull = new ConvexHullCollisionShape(corners);

        var mass = hull.ComputeMass(1);

        Assert.That(mass.Mass, Is.EqualTo(4.5f).Within(1e-4f), "a sixth of 27");
        Assert.That((mass.Center - new Vector3F(0.75f)).Length(), Is.LessThan(1e-5f));
        Assert.That(hull.FaceCount, Is.EqualTo(4));
    }

    [Test]
    public void PointsInOnePlane_AreRefused()
    {
        var flat = new[] { new Vector3F(0, 0, 0), new Vector3F(1, 0, 0), new Vector3F(0, 0, 1), new Vector3F(1, 0, 1) };

        Assert.Throws<ArgumentException>(() => _ = new ConvexHullCollisionShape(flat));
        Assert.Throws<ArgumentException>(() => _ = new ConvexHullCollisionShape([Vector3F.Zero, Vector3F.UnitX]));
    }

    [TestCase(0.01f)]
    [TestCase(0f)]
    [TestCase(-0.1f)]
    public void HullResting_OnAGroundBox_TouchesWithFourCorners(float gap)
    {
        var found = Collide(Cube(new Vector3F(0.5f)), new LocalPose(new Vector3F(0.2f, 0.5f + gap, -0.1f)),
            new BoxCollisionShape(5, 0.5f, 5), new LocalPose(new Vector3F(0, -0.5f, 0)));

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY);
        Assert.That(_manifold.Count, Is.EqualTo(4));
        for (var index = 0; index < _manifold.Count; index++)
        {
            Assert.That(_manifold.Separations[index], Is.EqualTo(gap).Within(1e-4f));
            Assert.That(_manifold.Positions[index].Y, Is.EqualTo(gap / 2).Within(1e-4f), "midway between the faces");
        }
    }

    [Test]
    public void HullFartherThanSpeculative_DoesNotTouch()
    {
        var found = Collide(Cube(new Vector3F(0.5f)), new LocalPose(new Vector3F(0, 1.1f, 0)), Cube(new Vector3F(0.5f)),
            LocalPose.Identity);

        Assert.That(found, Is.False);
    }

    // The same pair, as a box and as a hull of its corners: the specialized box contact and the general one agree.
    [Test]
    public void HullOfABox_TouchesAsTheBoxDoes([Range(0, 19)] int seed)
    {
        var random = new Random(seed);
        var half = new Vector3F(0.5f, 0.3f, 0.4f);
        var firstPose = new LocalPose(Vector3F.Zero, RandomTurn(random));
        var secondPose = new LocalPose(RandomPoint(random, 0.9f), RandomTurn(random));
        var box = new BoxCollisionShape(half);

        var asBoxes = Collide(box, firstPose, box, secondPose);
        var boxNormal = _manifold.Normal;
        var boxDeepest = Deepest();
        var asHulls = Collide(SharpCube(half), firstPose, SharpCube(half), secondPose);

        Assert.That(asHulls, Is.EqualTo(asBoxes));
        if (!asBoxes)
            return;

        Assert.That(Deepest(), Is.EqualTo(boxDeepest).Within(0.02f), "as deep");
        // Both keep a face over a near tie, each within its own tolerance: neither normal parts them much worse.
        Assert.That(SeparationAlong(box, firstPose, box, secondPose, _manifold.Normal),
            Is.GreaterThanOrEqualTo(SeparationAlong(box, firstPose, box, secondPose, boxNormal) - 0.01f));
    }

    private static float SeparationAlong(CollisionShape first, in LocalPose firstPose, CollisionShape second,
        in LocalPose secondPose, Vector3F normal)
    {
        var farthestOfFirst = Vector3F.Dot(firstPose.TransformPoint(first.GetSupport(firstPose.InverseTransformDirection(normal))), normal);
        var nearestOfSecond = Vector3F.Dot(secondPose.TransformPoint(second.GetSupport(secondPose.InverseTransformDirection(-normal))), normal);
        return nearestOfSecond - farthestOfFirst;
    }

    [Test]
    public void SphereOverAHullFace_TouchesItOnce()
    {
        var found = Collide(new SphereCollisionShape(0.5f), new LocalPose(new Vector3F(0.1f, 1.45f, 0)),
            Cube(new Vector3F(1)), LocalPose.Identity);

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.05f).Within(1e-4f));
    }

    [Test]
    public void SphereSunkIntoAHull_LeavesThroughTheNearestFace()
    {
        var found = Collide(Cube(new Vector3F(1)), LocalPose.Identity, new SphereCollisionShape(0.25f),
            new LocalPose(new Vector3F(0, 0, 0.9f)));

        Assert.That(found, Is.True);
        AssertNormal(Vector3F.UnitZ);
        Assert.That(Deepest(), Is.EqualTo(-0.35f).Within(1e-3f));
    }

    [Test]
    public void CapsuleLyingOnAHull_TouchesAtBothEnds()
    {
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);
        var found = Collide(new CapsuleCollisionShape(0.5f, 0.2f), new LocalPose(new Vector3F(0, 1.19f, 0), lying),
            Cube(new Vector3F(1)), LocalPose.Identity);

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY);
        Assert.That(_manifold.Count, Is.EqualTo(2));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(1e-4f));
        Assert.That(MathF.Abs(_manifold.Positions[0].X - _manifold.Positions[1].X), Is.EqualTo(1).Within(1e-4f));
    }

    [Test]
    public void HullsCrossedAtTheirEdges_TouchAtOnePoint()
    {
        var edgeDown = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 4);
        var edgeAcross = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 4);
        var reach = MathF.Sqrt(2) * 0.5f;
        var found = Collide(SharpCube(new Vector3F(0.5f)), new LocalPose(new Vector3F(0, 2 * reach - 0.01f, 0), edgeAcross),
            SharpCube(new Vector3F(0.5f)), new LocalPose(Vector3F.Zero, edgeDown));

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(1e-3f));
        AssertNormal(-Vector3F.UnitY, 1e-3f);
    }

    // Edges crossed just touching: no face parts them, so one point at no depth - not a face's plane cut deep in.
    [TestCase(0f)]
    [TestCase(1e-6f)]
    [TestCase(-1e-6f)]
    public void HullsCrossedAtTheirEdgesJustTouching_TouchAtOnePointAtNoDepth(float gap)
    {
        var edgeDown = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 4);
        var edgeAcross = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 4);
        var reach = MathF.Sqrt(2) * 0.5f;
        var found = Collide(SharpCube(new Vector3F(0.5f)), new LocalPose(new Vector3F(0, 2 * reach + gap, 0), edgeAcross),
            SharpCube(new Vector3F(0.5f)), new LocalPose(Vector3F.Zero, edgeDown));

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        Assert.That(MathF.Abs(_manifold.Separations[0]), Is.LessThan(1e-3f));
        AssertNormal(-Vector3F.UnitY, 1e-2f);
        Assert.That((_manifold.Positions[0] - new Vector3F(0, reach, 0)).Length(), Is.LessThan(1e-2f),
            "where the edges cross");
    }

    // Rounded edges crossed: each edge is cut by the radius times (root two less one), so they meet that much lower,
    // touching at one point with their cores apart - no EPA, no special case.
    [Test]
    public void RoundedHullsCrossedAtTheirEdges_TouchWhereTheRoundingMeets()
    {
        var edgeDown = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 4);
        var edgeAcross = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 4);
        var cube = Cube(new Vector3F(0.5f));
        var reach = MathF.Sqrt(2) * 0.5f - cube.ConvexRadius * (MathF.Sqrt(2) - 1);
        var found = Collide(cube, new LocalPose(new Vector3F(0, 2 * reach + 0.005f, 0), edgeAcross), cube,
            new LocalPose(Vector3F.Zero, edgeDown));

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        Assert.That(_manifold.Separations[0], Is.EqualTo(0.005f).Within(1e-4f));
        AssertNormal(-Vector3F.UnitY, 1e-4f);
        Assert.That((_manifold.Positions[0] - new Vector3F(0, reach + 0.0025f, 0)).Length(), Is.LessThan(1e-3f));
    }

    [Test]
    public void RoundedCube_KeepsItsFacesAndRoundsItsCorners()
    {
        var cube = Cube(new Vector3F(0.5f));

        Assert.That(cube.ConvexRadius, Is.EqualTo(ConvexHullCollisionShape.DefaultConvexRadius));
        Assert.That(cube.GetSupport(Vector3F.UnitY).Y, Is.EqualTo(0.5f).Within(1e-6f), "a face stands where it stood");
        var corner = Vector3F.Normalize(new Vector3F(1, 1, 1));
        var cut = MathF.Sqrt(3) * 0.5f - Vector3F.Dot(cube.GetSupport(corner), corner);
        Assert.That(cut, Is.EqualTo(cube.ConvexRadius * (MathF.Sqrt(3) - 1)).Within(1e-5f), "a corner is cut round");
    }

    // A sharp corner would be cut deep by the full radius: the radius shrinks so no corner loses more than it, and the
    // rounded hull stays within the hull every way.
    [Test]
    public void RoundedTetrahedron_IsRoundedLessAndStaysWithinItsHull()
    {
        Vector3F[] corners = [new(1, 1, 1), new(1, -1, -1), new(-1, 1, -1), new(-1, -1, 1)];
        var hull = new ConvexHullCollisionShape(corners);

        Assert.That(hull.ConvexRadius, Is.GreaterThan(0).And.LessThan(ConvexHullCollisionShape.DefaultConvexRadius));
        var random = new Random(3);
        for (var index = 0; index < 200; index++)
        {
            var direction = Vector3F.Normalize(RandomPoint(random, 1));
            var sharpest = float.MinValue;
            foreach (var corner in corners)
            {
                sharpest = MathF.Max(sharpest, Vector3F.Dot(corner, direction));
            }

            var rounded = Vector3F.Dot(hull.GetSupport(direction), direction);
            Assert.That(rounded, Is.LessThanOrEqualTo(sharpest + 1e-5f));
            Assert.That(rounded, Is.GreaterThanOrEqualTo(sharpest - ConvexHullCollisionShape.DefaultConvexRadius - 1e-5f));
        }
    }

    // The faces contacts meet are flat - faces within a hair of one plane joined, three ten-thousandths of the rock's
    // size - so a rock lying on one rests on all its corners; and each stands where a face of the hull does, the
    // radius in. Rounded or sharp alike.
    [Test]
    public void Rock_HasFlatFacesWhereTheHullHasThem([Range(0, 4)] int seed, [Values(0f, 0.05f)] float convexRadius)
    {
        var rock = Rock(new Random(seed), convexRadius);
        var minimum = new Vector3F(float.MaxValue);
        var maximum = new Vector3F(float.MinValue);
        foreach (var vertex in rock.Vertices)
        {
            minimum = Vector3F.Min(minimum, vertex);
            maximum = Vector3F.Max(maximum, vertex);
        }

        var hair = 3e-4f * Vector3F.Max(maximum - minimum) + 1e-6f;

        Assert.That(rock.ConvexRadius > 0, Is.EqualTo(convexRadius > 0));
        Span<Vector3F> corners = stackalloc Vector3F[ManifoldBuffer.FaceCapacity];
        for (var face = 0; face < rock.FlatFaceCount; face++)
        {
            var normal = rock.GetFlatFaceNormal(face);
            var offset = rock.GetFlatFaceOffset(face);
            var count = rock.GetFlatFace(face, corners);
            for (var corner = 0; corner < count; corner++)
            {
                Assert.That(Vector3F.Dot(normal, corners[corner]), Is.EqualTo(offset).Within(hair), $"face {face}");
            }

            // The faces moved in by the radius: rounded, the rock reaches each of them as far as the hull does.
            var hullReach = float.MinValue;
            foreach (var vertex in rock.Vertices)
            {
                hullReach = MathF.Max(hullReach, Vector3F.Dot(normal, vertex));
            }

            Assert.That(Vector3F.Dot(normal, rock.GetSupport(normal)), Is.EqualTo(hullReach).Within(hair), $"face {face}");
        }
    }

    // A rock lying on a face beside a sliver nearly in its plane bore on whichever was turned a hair nearer, and rocked
    // between them for good: each one dropped comes to rest.
    [Test]
    public void RocksDropped_EachComesToRest([Range(0, 19)] int seed, [Values(0f, 0.05f)] float convexRadius)
    {
        var random = new Random(seed);
        var rock = Rock(random, convexRadius);
        var scene = new SceneBuilder();
        scene.AddGround();
        var body = scene.World.Add(new BodyDescription
        {
            Shape = rock,
            Position = new Vector3<double>(0, 1, 0),
            Rotation = QuaternionF.Normalize(new QuaternionF((float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f))
        });

        scene.Run(10f);

        Assert.That(scene.World.IsAwake(body), Is.False);
    }

    [Test]
    public void ConvexRadius_BelowZeroOrNotFinite_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ConvexHullCollisionShape(CornersOf(new Vector3F(1)), -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ConvexHullCollisionShape(CornersOf(new Vector3F(1)), float.NaN));
    }

    // A hull a model keeps already: taken as it is, the same shape as from its points.
    [Test]
    public void HullBuiltAlready_GivesTheShapeItsPointsGive()
    {
        var points = CornersOf(new Vector3F(0.5f, 0.3f, 0.2f));
        var fromPoints = new ConvexHullCollisionShape(points);
        var fromHull = new ConvexHullCollisionShape(ConvexHull.FromPoints(points));

        Assert.That(fromHull.FaceCount, Is.EqualTo(fromPoints.FaceCount));
        Assert.That(fromHull.ConvexRadius, Is.EqualTo(fromPoints.ConvexRadius));
        Assert.That(fromHull.ComputeMass(1).Mass, Is.EqualTo(fromPoints.ComputeMass(1).Mass).Within(1e-5f));
        Assert.Throws<ArgumentException>(() =>
            _ = new ConvexHullCollisionShape(ConvexHull.FromPoints([Vector3F.Zero, Vector3F.UnitX, Vector3F.UnitY])));
    }

    // Round hulls of many corners, deep in each other at random: EPA takes many steps, and its nearest face still
    // tells about how deep, so they are pushed apart.
    [Test]
    public void RoundHullsDeepInEachOther_AreMeasuredDeep([Range(0, 9)] int seed)
    {
        var random = new Random(seed);
        var points = new List<Vector3F>();
        for (var index = 0; index < 600; index++)
        {
            points.Add(Vector3F.Normalize(new Vector3F((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f)) * 0.5f);
        }

        var ball = new ConvexHullCollisionShape(points);
        var offset = Vector3F.Normalize(RandomPoint(random, 1)) * 0.05f;

        var found = Collide(ball, LocalPose.Identity, ball, new LocalPose(offset, RandomTurn(random)));

        Assert.That(found, Is.True);
        Assert.That(Deepest(), Is.EqualTo(-0.95f).Within(0.05f), "about a ball's width, less the 5 cm between them");
    }

    [Test]
    public void HullDropped_RestsOnItsFaceAndSleeps()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var body = scene.World.Add(new BodyDescription
        {
            Shape = Cube(new Vector3F(0.5f)),
            Position = new Vector3<double>(0, 2, 0),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, 0.3f)
        });

        scene.Run(3f);

        var pose = scene.World.GetPose(body);
        Assert.That(pose.Position.Y, Is.EqualTo(0.5f).Within(0.02f));
        Assert.That(Vector3F.Transform(Vector3F.UnitY, pose.Rotation).Y, Is.GreaterThan(0.999f), "flat on its face");
        Assert.That(scene.World.IsAwake(body), Is.False);
    }

    [Test]
    public void TetrahedronDroppedOnAPoint_TipsOntoAFace()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var corners = new[] { new Vector3F(-0.5f, 0, -0.4f), new Vector3F(0.5f, 0, -0.4f), new Vector3F(0, 0, 0.5f),
            new Vector3F(0, 0.8f, 0) };
        var body = scene.World.Add(new BodyDescription
        {
            Shape = new ConvexHullCollisionShape(corners),
            Position = new Vector3<double>(0, 1.5f, 0),
            // Turned about a slanting axis, so it does not balance on an edge as a mirror-even fall would.
            Rotation = Quaternion.CreateFromAxisAngle(Vector3F.Normalize(new Vector3F(1, 0.2f, 0.3f)), MathF.PI * 0.9f)
        });

        scene.Run(4f);

        var points = new List<ContactPoint>();
        scene.World.GetContactPoints(points);
        var touching = points.FindAll(point => (point.First == body || point.Second == body) && point.Separation < 0.01f);
        Assert.That(touching.Count, Is.GreaterThanOrEqualTo(3), "it lies on a face, not a point or an edge");
        Assert.That(scene.World.GetLinearVelocity(body).Length(), Is.LessThan(0.05f));
    }

    [Test]
    public void StackOfHulls_Stands()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var bodies = new List<BodyHandle>();
        for (var level = 0; level < 5; level++)
        {
            bodies.Add(scene.World.Add(new BodyDescription
            {
                Shape = Cube(new Vector3F(0.5f)),
                Position = new Vector3<double>(0, 0.5f + level * 1.001f, 0)
            }));
        }

        scene.Run(4f);

        var top = scene.World.GetPose(bodies[^1]).Position;
        Assert.That(top.Y, Is.EqualTo(4.5f).Within(0.05f));
        Assert.That(Math.Abs(top.X) + Math.Abs(top.Z), Is.LessThan(0.05f));
    }

    [Test]
    public void RayAndOverlap_MeetAHull()
    {
        var world = new PhysicsWorld(new PhysicsSettings { Gravity = Vector3F.Zero });
        var body = world.Add(new BodyDescription { Kind = BodyKind.Static, Shape = Cube(new Vector3F(1)) });

        var hit = world.RayCast(new Vector3<double>(0.3f, 5, 0.2f), -Vector3F.UnitY, 10, out var rayHit);
        var found = new List<BodyHandle>();
        world.Overlap(new SphereCollisionShape(0.5f), new Pose(new Vector3<double>(1.4f, 0, 0)), found);
        var missed = world.RayCast(new Vector3<double>(1.5f, 5, 0), -Vector3F.UnitY, 10, out _);

        Assert.Multiple(() =>
        {
            Assert.That(hit, Is.True);
            Assert.That(rayHit.Distance, Is.EqualTo(4).Within(1e-4f));
            Assert.That((rayHit.Normal - Vector3F.UnitY).Length(), Is.LessThan(1e-5f));
            Assert.That(found, Is.EqualTo(new[] { body }));
            Assert.That(missed, Is.False);
        });
    }

    [Test]
    public void RayLeavingAHullFromItsSurface_DoesNotMeetIt()
    {
        var hull = Cube(new Vector3F(1));

        var leaving = hull.CastRay(new Ray<float>(new Vector3F(0.2f, 1, 0), Vector3F.UnitY), 10, out _, out _);
        var entering = hull.CastRay(new Ray<float>(new Vector3F(0.2f, 1, 0), -Vector3F.UnitY), 10, out var distance, out _);

        Assert.That(leaving, Is.False);
        Assert.That(entering, Is.True);
        Assert.That(distance, Is.Zero);
    }

    [TestCase(400)]
    [TestCase(2000)]
    public void HullOfManyPoints_KeepsAtMostItsCornerLimit_AndItsReach(int count)
    {
        var random = new Random(3);
        var points = new List<Vector3F>();
        for (var index = 0; index < count; index++)
        {
            points.Add(Vector3F.Normalize(new Vector3F((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f)));
        }

        var hull = new ConvexHullCollisionShape(points);

        Assert.That(hull.Vertices.Count, Is.LessThanOrEqualTo(ConvexHullCollisionShape.MaxCorners));
        Assert.That(hull.Vertices.Count, Is.GreaterThan(ConvexHullCollisionShape.MaxCorners * 3 / 4), "nearly as many");
        Assert.That(hull.BoundingRadius, Is.EqualTo(1).Within(1e-4f));
        Assert.That(hull.GetSupport(-Vector3F.UnitY).Y, Is.LessThan(-0.95f), "reaching down as far as up");
        Assert.That(hull.GetSupport(Vector3F.UnitY).Y, Is.GreaterThan(0.95f));
        Assert.That(hull.ComputeMass(1).Mass, Is.EqualTo(4f / 3 * MathF.PI).Within(0.2f), "nearly the ball's volume");
    }

    // A face of many corners clipped by the ground: the points kept spread over all of it, not the first few round it.
    [TestCase(32)]
    [TestCase(100)]
    public void ManyCorneredFaceOnTheGround_KeepsPointsSpreadOverIt(int sides)
    {
        var corners = new List<Vector3F>();
        for (var index = 0; index < sides; index++)
        {
            var angle = index * MathF.Tau / sides;
            corners.Add(new Vector3F(MathF.Cos(angle), -0.25f, MathF.Sin(angle)));
            corners.Add(new Vector3F(MathF.Cos(angle), 0.25f, MathF.Sin(angle)));
        }

        var found = Collide(new ConvexHullCollisionShape(corners), new LocalPose(new Vector3F(0, 0.24f, 0)),
            new BoxCollisionShape(5, 0.5f, 5), new LocalPose(new Vector3F(0, -0.5f, 0)));
        _manifold.ReduceTo(4);

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(4));
        var minimum = new Vector3F(float.MaxValue);
        var maximum = new Vector3F(float.MinValue);
        for (var index = 0; index < _manifold.Count; index++)
        {
            minimum = Vector3F.Min(minimum, _manifold.Positions[index]);
            maximum = Vector3F.Max(maximum, _manifold.Positions[index]);
        }

        Assert.That(maximum.X - minimum.X, Is.GreaterThan(1.4f), "across the disc");
        Assert.That(maximum.Z - minimum.Z, Is.GreaterThan(1.4f), "both ways");
    }

    [Test]
    public void HullFarFromTheOrigin_StillRestsOnItsFace()
    {
        var far = new Vector3F(1000, 0, -1000);
        var found = Collide(Cube(new Vector3F(0.5f)), new LocalPose(far + new Vector3F(0, 0.49f, 0)),
            new BoxCollisionShape(5, 0.5f, 5), new LocalPose(far + new Vector3F(0, -0.5f, 0)));

        Assert.That(found, Is.True);
        Assert.That(_manifold.Count, Is.EqualTo(4), "a face on a face, not one point");
        AssertNormal(-Vector3F.UnitY, 1e-3f);
    }

    private bool Collide(CollisionShape first, in LocalPose firstPose, CollisionShape second, in LocalPose secondPose) =>
        NarrowPhase.Collide(first, firstPose, second, secondPose, Speculative, _manifold);

    private float Deepest()
    {
        var deepest = float.MaxValue;
        for (var index = 0; index < _manifold.Count; index++)
        {
            deepest = MathF.Min(deepest, _manifold.Separations[index]);
        }

        return deepest;
    }

    private void AssertNormal(Vector3F expected, float tolerance = 1e-4f) =>
        Assert.That((_manifold.Normal - expected).Length(), Is.LessThan(tolerance), $"normal {_manifold.Normal}");

    private static ConvexHullCollisionShape Cube(Vector3F half) =>
        new(CornersOf(half));

    // A hull of a box's corners with no rounding: the box itself.
    private static ConvexHullCollisionShape SharpCube(Vector3F half) =>
        new(CornersOf(half), convexRadius: 0);

    // A flat rock of forty points, eighty centimeters across, rounded by default.
    private static ConvexHullCollisionShape Rock(Random random, float convexRadius = CollisionShape.DefaultConvexRadius)
    {
        var points = new List<Vector3F>();
        for (var index = 0; index < 40; index++)
        {
            points.Add(new Vector3F((float)random.NextDouble() - 0.5f, (float)random.NextDouble() * 0.6f - 0.3f,
                (float)random.NextDouble() - 0.5f) * 0.8f);
        }

        return new ConvexHullCollisionShape(points, convexRadius);
    }

    private static List<Vector3F> CornersOf(Vector3F half)
    {
        var corners = new List<Vector3F>();
        foreach (var x in new[] { -half.X, half.X })
        foreach (var y in new[] { -half.Y, half.Y })
        foreach (var z in new[] { -half.Z, half.Z })
        {
            corners.Add(new Vector3F(x, y, z));
        }

        return corners;
    }

    private static QuaternionF RandomTurn(Random random) =>
        QuaternionF.Normalize(new QuaternionF((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f,
            (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f));

    private static Vector3F RandomPoint(Random random, float reach) =>
        new(((float)random.NextDouble() * 2 - 1) * reach, ((float)random.NextDouble() * 2 - 1) * reach,
            ((float)random.NextDouble() * 2 - 1) * reach);
}
