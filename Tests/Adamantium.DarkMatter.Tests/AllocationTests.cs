using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>Steps, queries and changes of a warmed world allocate nothing: its buffers grow to what a scene needs while it
/// settles, and are reused from then on. All scenes but a sleeping one are kept awake, so collision and the solvers run.
/// </summary>
[TestFixture]
public class AllocationTests
{
    private const int WarmUpSteps = 600;
    private const int MeasuredSteps = 240;

    // Windows counted after the warm-up: a buffer reaching a new peak late allocates in one; a step that allocates, in
    // every one.
    private const int Windows = 3;

    [TestCase(AllocationScene.Boxes)]
    [TestCase(AllocationScene.Rocks)]
    [TestCase(AllocationScene.SpheresAndCapsules)]
    [TestCase(AllocationScene.Cylinders)]
    [TestCase(AllocationScene.HeightField)]
    [TestCase(AllocationScene.Mesh)]
    [TestCase(AllocationScene.Chain)]
    [TestCase(AllocationScene.Compounds)]
    [TestCase(AllocationScene.Asleep)]
    public void AWarmedWorld_StepsWithoutAllocating(AllocationScene scene)
    {
        var world = Build(scene);
        for (var step = 0; step < WarmUpSteps; step++)
        {
            world.Step();
        }

        var leastAllocated = long.MaxValue;
        for (var window = 0; window < Windows && leastAllocated > 0; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var step = 0; step < MeasuredSteps; step++)
            {
                world.Step();
            }

            leastAllocated = Math.Min(leastAllocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Multiple(() =>
        {
            Assert.That(leastAllocated, Is.Zero, "bytes in the quietest window");
            if (scene == AllocationScene.Asleep)
                Assert.That(world.LastStep.AwakeBodies, Is.Zero, "asleep while counted");
            else
                Assert.That(world.LastStep.AwakeBodies, Is.Positive, "kept awake while counted");

            // The chain hangs in the air, held by its joints alone.
            if (scene is not (AllocationScene.Asleep or AllocationScene.Chain))
                Assert.That(world.LastStep.TouchingPairs, Is.Positive, "touching while counted");
        });
    }

    [Test]
    public void Queries_AllocateNothing()
    {
        var world = Build(AllocationScene.Rocks);
        var field = Build(AllocationScene.HeightField);
        // Stepped, so the contact points have contacts to give.
        for (var step = 0; step < WarmUpSteps; step++)
        {
            world.Step();
            field.Step();
        }
        var rayFrom = new WorldPosition(0, 10, 0);
        var ball = new SphereCollisionShape(0.5f);
        var pair = new CompoundCollisionShape([
            new CompoundPart(ball, LocalPose.Identity), new CompoundPart(ball, new LocalPose(new Vector3F(1, 0, 0)))
        ]);
        var found = new List<BodyHandle>(64);
        var points = new List<ContactPoint>(4096);
        var above = new Pose(new WorldPosition(0, 10, 0));
        var down = new Vector3F(0, -15, 0);
        var onGround = new Pose(new WorldPosition(0, 0.3f, 0));
        var onField = new Pose(new WorldPosition(3, 0.2f, 3));
        // Each query is measured where it meets something, so its whole way runs, not the broad phase's early out.
        var met = false;

        Assert.Multiple(() =>
        {
            Assert.That(Allocated(() => met = world.RayCast(rayFrom, -Vector3F.UnitY, 20, out _)), Is.Zero, "ray");
            Assert.That(met, Is.True, "the ray met");
            Assert.That(Allocated(() => met = world.CastShape(ball, above, down, out _)), Is.Zero, "shape cast");
            Assert.That(met, Is.True, "the shape cast met");
            Assert.That(Allocated(() => met = world.CastShape(pair, above, down, out _)), Is.Zero, "compound cast");
            Assert.That(met, Is.True, "the compound cast met");
            Assert.That(Allocated(() => met = field.CastShape(ball, new Pose(new WorldPosition(3, 3, 3)),
                new Vector3F(0, -5, 0), out _)), Is.Zero, "cast onto a height field");
            Assert.That(met, Is.True, "the cast onto the field met");
            Assert.That(Allocated(() =>
            {
                found.Clear();
                world.Overlap(ball, onGround, found);
            }), Is.Zero, "overlap");
            Assert.That(found, Is.Not.Empty, "the overlap found");
            Assert.That(Allocated(() =>
            {
                found.Clear();
                world.Overlap(pair, onGround, found);
            }), Is.Zero, "compound overlap");
            Assert.That(found, Is.Not.Empty, "the compound overlap found");
            Assert.That(Allocated(() =>
            {
                found.Clear();
                field.Overlap(ball, onField, found);
            }), Is.Zero, "overlap with a height field");
            Assert.That(found, Is.Not.Empty, "the overlap with the field found");
            Assert.That(Allocated(() =>
            {
                points.Clear();
                world.GetContactPoints(points);
            }), Is.Zero, "contact points");
            Assert.That(points, Is.Not.Empty, "contact points were given");
        });
    }

    [Test]
    public void ABodysChanges_AllocateNothing()
    {
        var world = Build(AllocationScene.Boxes);
        var body = world.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(0.2f, 0.2f, 0.2f), Position = new WorldPosition(9, 9, 9)
        });
        var another = new BodyDescription
        {
            Shape = new BoxCollisionShape(0.2f, 0.2f, 0.2f), Position = new WorldPosition(-9, 9, 9)
        };

        Assert.Multiple(() =>
        {
            Assert.That(Allocated(() => world.SetLinearVelocity(body, Vector3F.UnitX)), Is.Zero, "velocity");
            Assert.That(Allocated(() => world.ApplyImpulse(body, Vector3F.UnitX, new WorldPosition(9, 9, 9))), Is.Zero,
                "impulse");
            Assert.That(Allocated(() => world.SetPose(body, new Pose(new WorldPosition(9, 9, 9)))), Is.Zero, "pose");
            Assert.That(Allocated(() => world.Remove(world.Add(another))), Is.Zero, "added and taken out");
        });
    }

    // What doing action a thousand times allocates, after doing it enough to warm it up.
    private static long Allocated(Action action)
    {
        for (var time = 0; time < 100; time++)
        {
            action();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var time = 0; time < 1000; time++)
        {
            action();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static IPhysicsWorld Build(AllocationScene scene)
    {
        // Kept awake but for the scene that is about sleeping: a settled pile asleep runs no collision or solver.
        var builder = new SceneBuilder(scene == AllocationScene.Asleep
            ? new PhysicsSettings()
            : new PhysicsSettings { TimeToSleep = float.MaxValue });
        var world = builder.World;
        switch (scene)
        {
            case AllocationScene.Boxes:
                builder.AddGround();
                for (var index = 0; index < 60; index++)
                {
                    builder.AddBox(new Vector3F(index % 6 * 1.1f - 3, 0.5f + index / 6 * 1.05f, 0), new Vector3F(0.5f));
                }

                break;
            case AllocationScene.Rocks:
                builder.AddGround();
                for (var index = 0; index < 60; index++)
                {
                    world.Add(new BodyDescription
                    {
                        Shape = new ConvexHullCollisionShape(Rock(index), 0.05f),
                        Position = new WorldPosition(index % 6 * 0.9f - 3, 0.6f + index / 6 * 0.9f, index % 5 * 0.3f - 0.6f)
                    });
                }

                break;
            case AllocationScene.SpheresAndCapsules:
                builder.AddGround();
                for (var index = 0; index < 40; index++)
                {
                    builder.AddSphere(new Vector3F(index % 5 - 2, 0.5f + index / 5, 0), 0.3f);
                    builder.AddCapsule(new Vector3F(index % 5 - 2, 0.5f + index / 5, 2), 0.3f, 0.2f);
                }

                break;
            case AllocationScene.Cylinders:
                builder.AddGround();
                for (var index = 0; index < 30; index++)
                {
                    world.Add(new BodyDescription
                    {
                        Shape = new CylinderCollisionShape(0.3f, 0.4f),
                        Position = new WorldPosition(index % 5 - 2, 0.5f + index / 5, 0)
                    });
                }

                break;
            case AllocationScene.HeightField:
                var heights = new float[32 * 32];
                for (var index = 0; index < heights.Length; index++)
                {
                    heights[index] = 0.2f * MathF.Sin(index * 0.3f);
                }

                world.Add(new BodyDescription
                {
                    Kind = BodyKind.Static, Shape = new HeightFieldCollisionShape(32, 32, heights, 0.5f, 0.5f)
                });
                for (var index = 0; index < 30; index++)
                {
                    builder.AddBox(new Vector3F(index % 5 - 2, 1 + index / 5, 0), new Vector3F(0.3f));
                }

                break;
            case AllocationScene.Mesh:
                Vector3F[] corners = [new(-8, 0, -8), new(-8, 0, 8), new(8, 0, 8), new(8, 0, -8)];
                world.Add(new BodyDescription
                {
                    Kind = BodyKind.Static, Shape = new TriangleMeshCollisionShape(corners, [0, 1, 2, 0, 2, 3])
                });
                for (var index = 0; index < 30; index++)
                {
                    builder.AddSphere(new Vector3F(index % 5 - 2, 1 + index / 5, 0), 0.3f);
                }

                break;
            case AllocationScene.Chain:
                var previous = default(BodyHandle);
                for (var index = 0; index < 20; index++)
                {
                    var link = builder.AddSphere(new Vector3F(0, 10 - index * 0.5f, 0), 0.1f);
                    world.Add(new JointDescription
                    {
                        Kind = JointKind.Ball, First = link, Second = previous,
                        Point = new WorldPosition(0, 10.25f - index * 0.5f, 0)
                    });
                    previous = link;
                }

                world.SetLinearVelocity(previous, new Vector3F(3, 0, 0));
                break;
            case AllocationScene.Compounds:
                builder.AddGround();
                for (var index = 0; index < 20; index++)
                {
                    world.Add(new BodyDescription
                    {
                        Shape = new CompoundCollisionShape([
                            new CompoundPart(new BoxCollisionShape(0.4f, 0.1f, 0.4f), LocalPose.Identity),
                            new CompoundPart(new SphereCollisionShape(0.2f), new LocalPose(new Vector3F(0, 0.3f, 0)))
                        ]),
                        Position = new WorldPosition(index % 5 - 2, 0.5f + index / 5, 0)
                    });
                }

                break;
            case AllocationScene.Asleep:
                builder.AddGround();
                for (var index = 0; index < 30; index++)
                {
                    builder.AddBox(new Vector3F(index % 6 - 3, 0.5f, index / 6 - 2), new Vector3F(0.45f));
                }

                builder.Run(5);
                break;
        }

        return world;
    }

    // A rock of twenty points scattered in a box of 0.8 meters, the same for a seed every time.
    private static Vector3F[] Rock(int seed)
    {
        var random = new Random(seed);
        var points = new Vector3F[20];
        for (var index = 0; index < points.Length; index++)
        {
            points[index] = new Vector3F((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f) * 0.8f;
        }

        return points;
    }
}
