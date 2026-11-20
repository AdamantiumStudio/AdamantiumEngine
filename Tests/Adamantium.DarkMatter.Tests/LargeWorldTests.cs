using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>A scene far from the world's origin behaves as the same scene at it: positions are world positions, and
/// contacts, joints and queries are worked out in float near the bodies they are about.</summary>
[TestFixture]
public class LargeWorldTests
{
    // Within a millimeter of the scene at the origin.
    private const double Millimeter = 1e-3;

    // Ten kilometers, a thousand kilometers, and a million: a level, a planet, a solar system.
    private static readonly double[] Offsets = [1e4, 1e6, 1e9];

    [TestCaseSource(nameof(Offsets))]
    public void AStack_StandsAsAtTheOrigin(double offset)
    {
        var near = BuildStack(WorldPosition.Zero, out var nearBoxes);
        var far = BuildStack(Far(offset), out var farBoxes);

        Run(near, 4);
        Run(far, 4);

        Assert.Multiple(() =>
        {
            for (var index = 0; index < nearBoxes.Count; index++)
            {
                var nearAt = near.GetPose(nearBoxes[index]).Position - WorldPosition.Zero;
                var farAt = far.GetPose(farBoxes[index]).Position - Far(offset);
                Assert.That((farAt - nearAt).Length(), Is.LessThan(Millimeter), $"box {index} at {offset} m");
                Assert.That(far.IsAwake(farBoxes[index]), Is.EqualTo(near.IsAwake(nearBoxes[index])),
                    $"box {index} asleep alike at {offset} m");
            }

            Assert.That(far.LastStep.TouchingPairs, Is.EqualTo(near.LastStep.TouchingPairs), $"touching at {offset} m");
        });
    }

    // A ball on a rod held to the world: the world's end of the rod is a world position too.
    [TestCaseSource(nameof(Offsets))]
    public void APendulumHeldToTheWorld_SwingsAsAtTheOrigin(double offset)
    {
        var near = BuildPendulum(WorldPosition.Zero, out var nearBall);
        var far = BuildPendulum(Far(offset), out var farBall);
        var pivot = Far(offset) + new Vector3<double>(0, 5, 0);
        var longest = 0.0;
        var shortest = double.MaxValue;
        for (var step = 0; step < 2 * 60; step++)
        {
            near.Step();
            far.Step();
            var length = (far.GetPose(farBall).Position - pivot).Length();
            longest = Math.Max(longest, length);
            shortest = Math.Min(shortest, length);
        }

        var nearAt = near.GetPose(nearBall).Position - WorldPosition.Zero;
        var farAt = far.GetPose(farBall).Position - Far(offset);
        Assert.Multiple(() =>
        {
            Assert.That((farAt - nearAt).Length(), Is.LessThan(Millimeter), $"where the ball is at {offset} m");
            Assert.That(longest, Is.LessThan(2.02), $"the rod at {offset} m");
            Assert.That(shortest, Is.GreaterThan(1.98), $"the rod at {offset} m");
        });
    }

    [TestCaseSource(nameof(Offsets))]
    public void Queries_MeetAsAtTheOrigin(double offset)
    {
        var at = Far(offset);
        var world = BuildStack(at, out var boxes);
        Run(world, 1);
        var ball = new SphereCollisionShape(0.25f);
        var found = new List<BodyHandle>();

        var rayMet = world.RayCast(at + new Vector3<double>(0.1, 20, 0.1), -Vector3F.UnitY, 40, out var rayHit);
        var castMet = world.CastShape(ball, new Pose(at + new Vector3<double>(3, 0.5, 0)), new Vector3F(-6, 0, 0),
            out var castHit);
        world.Overlap(ball, new Pose(at + new Vector3<double>(0, 0.5, 0.6)), found);
        var points = new List<ContactPoint>();
        world.GetContactPoints(points);

        Assert.Multiple(() =>
        {
            Assert.That(rayMet, Is.True);
            Assert.That(rayHit.Body, Is.EqualTo(boxes[^1]), "the top box");
            Assert.That(rayHit.Position.Y - at.Y, Is.EqualTo(5).Within(0.01), "its top face");
            Assert.That(castMet, Is.True);
            Assert.That(castHit.Position.X - at.X, Is.EqualTo(0.5).Within(0.01), "a box's side");
            Assert.That(castHit.Distance, Is.EqualTo(2.25f).Within(0.01f));
            Assert.That(found, Has.Count.EqualTo(1), "the bottom box beside the ball");
            Assert.That(points, Is.Not.Empty);
            foreach (var point in points)
            {
                Assert.That((point.Position - at).Length(), Is.LessThan(5), "a contact point stands where the stack is");
            }
        });
    }

    // A ground two hundred kilometers wide, added first, and a crate ninety out from its middle: it rests there as it
    // does near the middle.
    [Test]
    public void ACrateFarOutOnAHugeGround_RestsAsNearItsMiddle()
    {
        var farOutAt = new Vector3<double>(90_000, 0, -90_000);
        var middle = BuildCrateOnHugeGround(WorldPosition.Zero, out var middleCrate);
        var farOut = BuildCrateOnHugeGround(WorldPosition.Zero + farOutAt, out var farCrate);

        Run(middle, 3);
        Run(farOut, 3);

        var middleAt = middle.GetPose(middleCrate).Position - WorldPosition.Zero;
        var farAt = farOut.GetPose(farCrate).Position - WorldPosition.Zero - farOutAt;
        Assert.Multiple(() =>
        {
            Assert.That((farAt - middleAt).Length(), Is.LessThan(Millimeter));
            Assert.That(farOut.IsAwake(farCrate), Is.EqualTo(middle.IsAwake(middleCrate)), "asleep alike");
        });
    }

    private static PhysicsWorld BuildCrateOnHugeGround(WorldPosition crateAt, out BodyHandle crate)
    {
        var world = new PhysicsWorld();
        world.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(100_000, 1, 100_000),
            Position = new WorldPosition(0, -1, 0)
        });
        crate = world.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(0.5f, 0.5f, 0.5f), Position = crateAt + new Vector3<double>(0, 0.51, 0),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, 0.3f)
        });
        return world;
    }

    // Diagonal, so every axis is far.
    private static WorldPosition Far(double offset) => new(offset, offset * 0.5, -offset);

    private static PhysicsWorld BuildStack(WorldPosition at, out List<BodyHandle> boxes)
    {
        var world = new PhysicsWorld();
        world.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(50, 1, 50), Position = at + new Vector3<double>(0, -1, 0)
        });
        boxes = [];
        for (var level = 0; level < 5; level++)
        {
            boxes.Add(world.Add(new BodyDescription
            {
                Shape = new BoxCollisionShape(0.5f, 0.5f, 0.5f), Position = at + new Vector3<double>(0, 0.5 + level, 0)
            }));
        }

        return world;
    }

    private static PhysicsWorld BuildPendulum(WorldPosition at, out BodyHandle ball)
    {
        var world = new PhysicsWorld();
        ball = world.Add(new BodyDescription
        {
            Shape = new SphereCollisionShape(0.1f), Position = at + new Vector3<double>(2, 5, 0)
        });
        world.Add(new JointDescription
        {
            Kind = JointKind.Distance, First = ball, Point = at + new Vector3<double>(2, 5, 0),
            SecondPoint = at + new Vector3<double>(0, 5, 0)
        });
        return world;
    }

    private static void Run(IPhysicsWorld world, float seconds)
    {
        for (var step = 0; step < seconds * world.Settings.StepRate; step++)
        {
            world.Step();
        }
    }
}
