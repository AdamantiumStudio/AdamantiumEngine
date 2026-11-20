using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>The reference scenes of the engine: each must keep behaving as here for the engine's whole life.</summary>
[TestFixture]
public class ReferenceSceneTests
{
    private const float Gravity = 9.81f;

    [Test]
    public void FreeFall_FollowsGravity()
    {
        var scene = new SceneBuilder();
        var ball = scene.AddSphere(new Vector3F(0, 10, 0), 0.5f);

        scene.Run(1f);

        var pose = scene.World.GetPose(ball);
        Assert.That(pose.Position.Y, Is.EqualTo(10 - 0.5f * Gravity).Within(0.02f));
        Assert.That(scene.World.GetLinearVelocity(ball).Y, Is.EqualTo(-Gravity).Within(0.001f));
    }

    [Test]
    public void BoxOnGround_StaysPutAndFallsAsleep()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));

        scene.Run(5f);

        var pose = scene.World.GetPose(box);
        Assert.That(pose.Position.Y, Is.EqualTo(0.5f).Within(0.01f));
        Assert.That(Math.Abs(pose.Position.X) + Math.Abs(pose.Position.Z), Is.LessThan(0.001f));
        Assert.That(TiltOf(pose), Is.LessThan(0.01f));
        Assert.That(scene.World.IsAwake(box), Is.False);
    }

    [Test]
    public void StackOfTenBoxes_Stands()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var boxes = new List<BodyHandle>();
        for (var level = 0; level < 10; level++)
        {
            boxes.Add(scene.AddBox(new Vector3F(0, 0.5f + level, 0), new Vector3F(0.5f)));
        }

        scene.Run(10f);

        var top = scene.World.GetPose(boxes[^1]).Position;
        Assert.That(top.Y, Is.EqualTo(9.5f).Within(0.05f));
        Assert.That(Math.Abs(top.X) + Math.Abs(top.Z), Is.LessThan(0.05f));
        AssertNoDeepOverlap(scene.World);
        foreach (var box in boxes)
        {
            Assert.That(scene.World.IsAwake(box), Is.False, "the stack comes to rest and sleeps");
        }
    }

    [Test]
    public void PyramidOfFiftyFiveBoxes_Stands()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var boxes = BuildPyramid(scene, 10);
        var starts = new List<WorldPosition>();
        foreach (var box in boxes)
        {
            starts.Add(scene.World.GetPose(box).Position);
        }

        scene.Run(10f);

        for (var index = 0; index < boxes.Count; index++)
        {
            var moved = scene.World.GetPose(boxes[index]).Position - starts[index];
            Assert.That(moved.Length(), Is.LessThan(0.1f), $"box {index} moved {moved}");
        }

        AssertNoDeepOverlap(scene.World);
    }

    [TestCase(0.6f, false)]
    [TestCase(0.2f, true)]
    public void BoxOnSlope_SlidesOnlyBelowTheFrictionLimit(float friction, bool slides)
    {
        var slope = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, 20f * MathF.PI / 180f);
        var scene = new SceneBuilder();
        scene.AddGround(friction, slope);
        var start = Vector3F.Transform(new Vector3F(0, 0.5f, 0), slope);
        var box = scene.AddBox(start, new Vector3F(0.5f), friction, rotation: slope);

        scene.Run(3f);

        var moved = (scene.World.GetPose(box).Position - new WorldPosition(start)).Length();
        if (slides)
            Assert.That(moved, Is.GreaterThan(1f));
        else
            Assert.That(moved, Is.LessThan(0.05f));
    }

    [Test]
    public void Ball_BouncesToTheHeightItsRestitutionKeeps()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var ball = scene.AddSphere(new Vector3F(0, 2.5f, 0), 0.5f, restitution: 0.5f);

        var bounced = false;
        var highest = 0f;
        for (var step = 0; step < 3 * scene.World.Settings.StepRate; step++)
        {
            scene.World.Step();
            var velocity = scene.World.GetLinearVelocity(ball).Y;
            if (!bounced && velocity > 0)
                bounced = true;

            if (bounced)
                highest = MathF.Max(highest, (float)scene.World.GetPose(ball).Position.Y);

            if (bounced && velocity < 0)
                break;
        }

        Assert.That(bounced, Is.True);
        Assert.That(highest - 0.5f, Is.EqualTo(0.25f * 2f).Within(0.15f));
    }

    [Test]
    public void RowOfDominoes_FallsToTheLast()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var dominoes = new List<BodyHandle>();
        for (var index = 0; index < 10; index++)
        {
            dominoes.Add(scene.AddBox(new Vector3F(index * 0.6f, 0.5f, 0), new Vector3F(0.05f, 0.5f, 0.25f)));
        }

        scene.World.ApplyImpulse(dominoes[0], new Vector3F(15f, 0, 0), new WorldPosition(0, 0.95f, 0));
        scene.Run(6f);

        var up = Vector3F.Transform(Vector3F.UnitY, scene.World.GetPose(dominoes[^1]).Rotation);
        Assert.That(up.Y, Is.LessThan(0.7f), "the last domino is down");
    }

    [Test]
    public void CapsuleLyingOnGround_RestsOnTwoPoints()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);
        var capsule = scene.AddCapsule(new Vector3F(0, 0.3f, 0), 0.5f, 0.3f, lying);

        scene.Run(3f);

        Assert.That(scene.World.GetPose(capsule).Position.Y, Is.EqualTo(0.3f).Within(0.01f));
        var length = Vector3F.Transform(Vector3F.UnitY, scene.World.GetPose(capsule).Rotation);
        Assert.That(MathF.Abs(length.Y), Is.LessThan(0.02f), "the capsule still lies flat");
        var points = new List<ContactPoint>();
        scene.World.GetContactPoints(points);
        Assert.That(points, Has.Count.EqualTo(2));
    }

    [Test]
    public void SleepingBox_WakesWhenStruck()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(2f);
        Assert.That(scene.World.IsAwake(box), Is.False);

        scene.AddSphere(new Vector3F(0, 3f, 0), 0.25f);
        var woke = false;
        for (var step = 0; step < 2 * scene.World.Settings.StepRate && !woke; step++)
        {
            scene.World.Step();
            woke = scene.World.IsAwake(box);
        }

        Assert.That(woke, Is.True);
    }

    [Test]
    public void FastBox_StopsOnAThinFloor()
    {
        var scene = new SceneBuilder();
        scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static,
            Shape = new Shapes.BoxCollisionShape(5, 0.025f, 5),
            Position = new WorldPosition(0, -0.025f, 0)
        });
        var box = scene.AddBox(new Vector3F(0, 3f, 0), new Vector3F(0.25f), velocity: new Vector3F(0, -40f, 0));

        scene.Run(2f);

        Assert.That(scene.World.GetPose(box).Position.Y, Is.EqualTo(0.25f).Within(0.02f));
    }

    [Test]
    public void LargePyramidKeptAwake_Stands()
    {
        var scene = new SceneBuilder(new PhysicsSettings { TimeToSleep = float.MaxValue });
        scene.AddGround();
        var boxes = BuildPyramid(scene, 20);
        var top = scene.World.GetPose(boxes[^1]).Position;
        var slowest = 0.0;
        var total = 0.0;
        var measured = 0;
        for (var step = 0; step < 5 * scene.World.Settings.StepRate; step++)
        {
            scene.World.Step();
            if (step < 300)
                continue;

            measured++;
            total += scene.World.LastStep.Milliseconds;
            slowest = Math.Max(slowest, scene.World.LastStep.Milliseconds);
        }

        TestContext.Progress.WriteLine(
            $"210 boxes awake: mean step {total / measured:F3} ms, slowest {slowest:F2} ms, {scene.World.LastStep.TouchingPairs} touching pairs");
        Assert.That((scene.World.GetPose(boxes[^1]).Position - top).Length(), Is.LessThan(0.1f));
        AssertNoDeepOverlap(scene.World);
    }

    [Test]
    public void SameScene_StepsToTheSameResult()
    {
        var first = RunPyramid();
        var second = RunPyramid();

        Assert.That(second, Is.EqualTo(first));
    }

    private static List<WorldPosition> RunPyramid()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var boxes = BuildPyramid(scene, 6);
        scene.AddSphere(new Vector3F(0.3f, 9f, 0.1f), 0.4f);
        scene.Run(3f);
        var positions = new List<WorldPosition>();
        foreach (var box in boxes)
        {
            positions.Add(scene.World.GetPose(box).Position);
        }

        return positions;
    }

    private static List<BodyHandle> BuildPyramid(SceneBuilder scene, int baseCount)
    {
        var boxes = new List<BodyHandle>();
        for (var row = 0; row < baseCount; row++)
        {
            var count = baseCount - row;
            for (var column = 0; column < count; column++)
            {
                var x = (column - (count - 1) * 0.5f) * 1.02f;
                boxes.Add(scene.AddBox(new Vector3F(x, 0.5f + row, 0), new Vector3F(0.5f)));
            }
        }

        return boxes;
    }

    private static void AssertNoDeepOverlap(IPhysicsWorld world)
    {
        var points = new List<ContactPoint>();
        world.GetContactPoints(points);
        foreach (var point in points)
        {
            Assert.That(point.Separation, Is.GreaterThan(-0.02f), $"overlap at {point.Position}");
        }
    }

    private static float TiltOf(Pose pose)
    {
        var up = Vector3F.Transform(Vector3F.UnitY, pose.Rotation);
        return MathF.Acos(Math.Clamp(up.Y, -1f, 1f));
    }
}
