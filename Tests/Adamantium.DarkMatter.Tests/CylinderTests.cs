using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.CollisionDetection;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>Cylinders: their mass, bounds and rays, their contacts by the general path - standing, lying, on a rim -
/// and a wheel rolling down a slope.</summary>
[TestFixture]
public class CylinderTests
{
    private const float Speculative = 0.02f;
    private readonly ManifoldBuffer _manifold = new();

    [Test]
    public void Cylinder_WeighsAsASolidCylinder()
    {
        var cylinder = new CylinderCollisionShape(1, 0.5f);

        var mass = cylinder.ComputeMass(1000);

        var expected = 1000 * MathF.PI * 0.25f * 2;
        Assert.That(mass.Mass, Is.EqualTo(expected).Within(1e-3f * expected));
        Assert.That(mass.Inertia.Y, Is.EqualTo(expected * 0.25f / 2).Within(1e-3f * expected));
        Assert.That(mass.Inertia.X, Is.EqualTo(expected * (3 * 0.25f + 4) / 12).Within(1e-3f * expected));
    }

    [Test]
    public void ConvexRadius_IsKeptWithinAQuarterOfTheSmallerSize_AndRefusedBelowZero()
    {
        Assert.That(new CylinderCollisionShape(1, 0.5f).ConvexRadius, Is.EqualTo(CollisionShape.DefaultConvexRadius));
        Assert.That(new CylinderCollisionShape(0.08f, 0.5f).ConvexRadius, Is.EqualTo(0.02f).Within(1e-6f));
        Assert.That(new CylinderCollisionShape(1, 0.5f, 0).ConvexRadius, Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new CylinderCollisionShape(1, 0.5f, -1));
    }

    [Test]
    public void TurnedCylinder_StaysWithinItsBounds()
    {
        var cylinder = new CylinderCollisionShape(0.7f, 0.3f);
        var pose = new Pose(new Vector3<double>(1, 2, 3),
            Quaternion.CreateFromAxisAngle(Vector3F.Normalize(new Vector3F(1, 0.4f, -0.3f)), 0.8f));
        var bounds = cylinder.ComputeBounds(pose);
        var random = new Random(5);
        for (var index = 0; index < 100; index++)
        {
            var direction = Vector3F.Normalize(new Vector3F((float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f));
            var point = pose.TransformPoint(cylinder.GetSupport(pose.InverseTransformDirection(direction)));
            Assert.That(bounds.Contains(point), Is.Not.EqualTo(ContainmentType.Disjoint), $"{point}");
        }

        Assert.That(bounds.Maximum.X - bounds.Minimum.X, Is.LessThan(2 * cylinder.BoundingRadius));
    }

    [TestCase(0.01f)]
    [TestCase(0f)]
    [TestCase(-0.05f)]
    public void CylinderStandingOnItsEnd_TouchesRoundTheRim(float gap)
    {
        var found = Collide(new CylinderCollisionShape(0.5f, 0.4f), new LocalPose(new Vector3F(0.3f, 0.5f + gap, -0.2f)),
            Ground(), GroundPose());

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY);
        Assert.That(_manifold.Count, Is.EqualTo(CylinderCollisionShape.EndCorners));
        for (var index = 0; index < _manifold.Count; index++)
        {
            Assert.That(_manifold.Separations[index], Is.EqualTo(gap).Within(1e-4f));
            var across = new Vector3F(_manifold.Positions[index].X - 0.3f, 0, _manifold.Positions[index].Z + 0.2f);
            Assert.That(across.Length(), Is.EqualTo(0.4f - CollisionShape.DefaultConvexRadius).Within(1e-3f),
                "on the flat of the end, inside the rounded rim");
        }
    }

    [TestCase(0.01f)]
    [TestCase(-0.05f)]
    public void CylinderLyingOnItsSide_TouchesAlongALine(float gap)
    {
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2);
        var found = Collide(new CylinderCollisionShape(0.5f, 0.4f), new LocalPose(new Vector3F(0, 0.4f + gap, 0), lying),
            Ground(), GroundPose());

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY);
        Assert.That(_manifold.Count, Is.EqualTo(2));
        Assert.That(_manifold.Separations[0], Is.EqualTo(gap).Within(1e-4f));
        Assert.That(_manifold.Separations[1], Is.EqualTo(gap).Within(1e-4f));
        Assert.That(MathF.Abs(_manifold.Positions[0].Z - _manifold.Positions[1].Z),
            Is.EqualTo(2 * (0.5f - CollisionShape.DefaultConvexRadius)).Within(1e-3f));
    }

    // Tilted onto its rim, it touches round the rounding where the rim is lowest.
    [Test]
    public void CylinderOnItsRim_TouchesWhereTheRimIsLowest()
    {
        var cylinder = new CylinderCollisionShape(0.5f, 0.4f);
        var tilt = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, 0.6f);
        var lowestPoint = Vector3F.Transform(cylinder.GetSupport(Vector3F.Transform(-Vector3F.UnitY,
            QuaternionF.Conjugate(tilt))), tilt);
        var lowest = -lowestPoint.Y;
        var found = Collide(cylinder, new LocalPose(new Vector3F(0, lowest + 0.005f, 0), tilt), Ground(), GroundPose());

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY, 1e-3f);
        var deepest = 0;
        for (var index = 1; index < _manifold.Count; index++)
        {
            if (_manifold.Separations[index] < _manifold.Separations[deepest])
                deepest = index;
        }

        Assert.That(_manifold.Separations[deepest], Is.EqualTo(0.005f).Within(1e-3f));
        Assert.That(_manifold.Positions[deepest].X, Is.EqualTo(lowestPoint.X).Within(0.02f));
    }

    // Logs lying along each other touch along a line, not at one point they could turn about: the ends of where they
    // overlap.
    [Test]
    public void LogsLyingAlongEachOther_TouchAtBothEndsOfTheirOverlap()
    {
        var log = new CylinderCollisionShape(1, 0.3f);
        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2);

        var found = Collide(log, new LocalPose(new Vector3F(0, 0.59f, 0.5f), lying), log, new LocalPose(Vector3F.Zero, lying));

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY, 1e-3f);
        Assert.That(_manifold.Count, Is.EqualTo(2));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(1e-3f));
        Assert.That(_manifold.Separations[1], Is.EqualTo(-0.01f).Within(1e-3f));
        var reach = 1 - CollisionShape.DefaultConvexRadius;
        Assert.That(MathF.Abs(_manifold.Positions[0].Z - _manifold.Positions[1].Z), Is.EqualTo(2 * reach - 0.5f).Within(1e-3f));
    }

    [Test]
    public void SphereOnAnEnd_AndCapsuleAcrossIt_TouchItsFace()
    {
        var cylinder = new CylinderCollisionShape(0.5f, 0.6f);
        var found = Collide(new SphereCollisionShape(0.3f), new LocalPose(new Vector3F(0.1f, 0.79f, 0)), cylinder,
            LocalPose.Identity);

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY, 1e-3f);
        Assert.That(_manifold.Count, Is.EqualTo(1));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(1e-3f));

        var lying = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);
        found = Collide(new CapsuleCollisionShape(0.3f, 0.1f), new LocalPose(new Vector3F(0, 0.59f, 0), lying), cylinder,
            LocalPose.Identity);

        Assert.That(found, Is.True);
        AssertNormal(-Vector3F.UnitY, 1e-3f);
        Assert.That(_manifold.Count, Is.EqualTo(2));
        Assert.That(_manifold.Separations[0], Is.EqualTo(-0.01f).Within(1e-3f));
    }

    [Test]
    public void CylindersStacked_EndOnEnd_Stand()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var cylinders = new List<BodyHandle>();
        for (var level = 0; level < 4; level++)
        {
            cylinders.Add(scene.World.Add(new BodyDescription
            {
                Shape = new CylinderCollisionShape(0.3f, 0.4f),
                Position = new Vector3<double>(0, 0.3f + level * 0.601f, 0)
            }));
        }

        scene.Run(4f);

        var top = scene.World.GetPose(cylinders[^1]);
        Assert.That(top.Position.Y, Is.EqualTo(0.3f + 3 * 0.6f).Within(0.03f));
        Assert.That(Vector3F.Transform(Vector3F.UnitY, top.Rotation).Y, Is.GreaterThan(0.999f));
        Assert.That(scene.World.IsAwake(cylinders[^1]), Is.False);
    }

    // A wheel on a slope rolls without slipping: it speeds up at two thirds of g along the slope, turning as fast as it
    // goes over its radius.
    [Test]
    public void WheelOnASlope_RollsWithoutSlipping()
    {
        var slope = 0.35f;
        var scene = new SceneBuilder();
        scene.AddGround(rotation: Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, slope));
        var downhill = Vector3F.Transform(-Vector3F.UnitX, Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, slope));
        var up = Vector3F.Transform(Vector3F.UnitY, Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, slope));
        var wheel = scene.World.Add(new BodyDescription
        {
            Shape = new CylinderCollisionShape(0.15f, 0.4f),
            Position = (up * 0.401f).As<double>(),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2),
            Friction = 1
        });

        scene.Run(1f);

        var speed = Vector3F.Dot(scene.World.GetLinearVelocity(wheel), downhill);
        var expected = 2f / 3f * 9.81f * MathF.Sin(slope);
        Assert.That(speed, Is.EqualTo(expected).Within(0.1f * expected));
        Assert.That(scene.World.GetAngularVelocity(wheel).Length() * 0.4f, Is.EqualTo(speed).Within(0.05f * speed),
            "rolling, not sliding");
    }

    [Test]
    public void CylinderSpinningAboutItsAxis_SweepsNothing()
    {
        var cylinder = new CylinderCollisionShape(0.15f, 0.4f);
        var turn = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2);
        var axis = Vector3F.Transform(Vector3F.UnitY, turn);

        Assert.That(cylinder.MeasureSweepSpeed(axis * 50, turn), Is.EqualTo(0).Within(1e-4f));
        Assert.That(cylinder.MeasureSweepSpeed(Vector3F.Cross(axis, Vector3F.UnitY) * 2, turn), Is.GreaterThan(0.5f));
    }

    [Test]
    public void Ray_MeetsTheSideAndTheEnds()
    {
        var cylinder = new CylinderCollisionShape(1, 0.5f);

        Assert.That(cylinder.CastRay(new Ray<float>(new Vector3F(-3, 0.2f, 0), Vector3F.UnitX), 10, out var distance,
            out var normal), Is.True);
        Assert.That(distance, Is.EqualTo(2.5f).Within(1e-5f));
        Assert.That(normal, Is.EqualTo(-Vector3F.UnitX));

        Assert.That(cylinder.CastRay(new Ray<float>(new Vector3F(0.2f, 4, 0), -Vector3F.UnitY), 10, out distance,
            out normal), Is.True);
        Assert.That(distance, Is.EqualTo(3).Within(1e-5f));
        Assert.That(normal, Is.EqualTo(Vector3F.UnitY));

        Assert.That(cylinder.CastRay(new Ray<float>(new Vector3F(0.6f, 4, 0), -Vector3F.UnitY), 10, out _, out _),
            Is.False, "beside the end");
        Assert.That(cylinder.CastRay(new Ray<float>(new Vector3F(-3, 1.2f, 0), Vector3F.UnitX), 10, out _, out _),
            Is.False, "above the side");
    }

    private bool Collide(CollisionShape first, in LocalPose firstPose, CollisionShape second,
        in LocalPose secondPose) =>
        NarrowPhase.Collide(first, firstPose, second, secondPose, Speculative, _manifold);

    private static BoxCollisionShape Ground() => new(5, 0.5f, 5);

    private static LocalPose GroundPose() => new(new Vector3F(0, -0.5f, 0));

    private void AssertNormal(Vector3F expected, float tolerance = 1e-4f) =>
        Assert.That((_manifold.Normal - expected).Length(), Is.LessThan(tolerance), $"normal {_manifold.Normal}");
}
