using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Dynamics;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

[TestFixture]
public class CompoundTests
{
    [Test]
    public void Diagonalize_GivesTheMomentsAndAxesATensorWasMadeOf()
    {
        var axes = QuaternionF.Normalize(new QuaternionF(0.3f, -0.5f, 0.2f, 0.8f));
        var moments = new Vector3F(2f, 5f, 9f);
        var tensor = InertiaMath.ToTensor(moments, axes, 0, Vector3<double>.Zero);

        InertiaMath.Diagonalize(tensor, out var found, out var foundAxes);
        var rebuilt = InertiaMath.ToTensor(found, foundAxes, 0, Vector3<double>.Zero);

        Assert.That(new[] { found.X, found.Y, found.Z }, Is.EquivalentTo(new[] { 2f, 5f, 9f }).Using<float>(NearlyEqual));
        AssertNear(rebuilt, tensor);
        Assert.That(foundAxes.Length(), Is.EqualTo(1f).Within(1e-5f));
    }

    [Test]
    public void TwoBoxesSideBySide_WeighAndTurnAsOneTwiceAsLong()
    {
        var half = new BoxCollisionShape(0.5f, 0.5f, 0.5f);
        var compound = new CompoundCollisionShape([
            new CompoundPart(half, new LocalPose(new Vector3F(-0.5f, 0, 0))),
            new CompoundPart(half, new LocalPose(new Vector3F(0.5f, 0, 0)))
        ]);
        var whole = new BoxCollisionShape(1f, 0.5f, 0.5f).ComputeMass(1000);

        var mass = compound.ComputeMass(1000);

        Assert.That(mass.Mass, Is.EqualTo(whole.Mass).Within(1e-3f));
        Assert.That(mass.Center.Length(), Is.LessThan(1e-6f));
        var found = Vector3F.Transform(new Vector3F(mass.Inertia.X, 0, 0), mass.InertiaRotation);
        Assert.That(MathF.Abs(found.X), Is.EqualTo(whole.Inertia.X).Within(whole.Inertia.X * 1e-4f),
            "the moment around the long axis");
    }

    [Test]
    public void CenterOfMass_IsWhereTheMassIs()
    {
        var heavy = new BoxCollisionShape(new Vector3F(0.5f));
        var light = new SphereCollisionShape(0.1f);
        var compound = new CompoundCollisionShape([
            new CompoundPart(heavy, new LocalPose(new Vector3F(2, 0, 0))),
            new CompoundPart(light, new LocalPose(new Vector3F(-2, 0, 0)))
        ]);
        var heavyMass = heavy.ComputeMass(1000).Mass;
        var lightMass = light.ComputeMass(1000).Mass;

        var center = compound.ComputeMass(1000).Center;

        Assert.That(center.X, Is.EqualTo((2 * heavyMass - 2 * lightMass) / (heavyMass + lightMass)).Within(1e-4f));
    }

    [Test]
    public void Compound_IsRefusedEmptyNestedOrWithoutAShape()
    {
        var box = new BoxCollisionShape(new Vector3F(0.5f));
        var inner = new CompoundCollisionShape([new CompoundPart(box, LocalPose.Identity)]);

        Assert.Throws<ArgumentException>(() => _ = new CompoundCollisionShape([]));
        Assert.Throws<ArgumentException>(() => _ = new CompoundCollisionShape([new CompoundPart(inner, LocalPose.Identity)]));
        Assert.Throws<ArgumentException>(() => _ = new CompoundCollisionShape([new CompoundPart(null, LocalPose.Identity)]));
        Assert.Throws<ArgumentException>(() =>
            _ = new CompoundCollisionShape([new CompoundPart(box, new LocalPose(Vector3F.Zero, default))]));
    }

    [Test]
    public void BodyOfAShapeOffItsOrigin_RestsWhereTheShapeIs()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var body = scene.World.Add(new BodyDescription
        {
            Shape = new CompoundCollisionShape([new CompoundPart(new BoxCollisionShape(new Vector3F(0.5f)), new LocalPose(new Vector3F(3, 0, 0)))]),
            Position = new WorldPosition(0, 1, 0)
        });

        scene.Run(2f);

        var pose = scene.World.GetPose(body);
        Assert.That(pose.Position.X, Is.EqualTo(0).Within(0.01f), "the origin stays where the shape stands off it");
        Assert.That(pose.Position.Y, Is.EqualTo(0.5f).Within(0.02f));
        Assert.That(scene.World.GetCenterOfMass(body).X, Is.EqualTo(3).Within(0.01f));
        Assert.That((scene.World.GetLocalCenterOfMass(body) - new Vector3F(3, 0, 0)).Length(), Is.LessThan(1e-6f));
        Assert.That(scene.World.GetBounds(body).Center.X, Is.EqualTo(3).Within(0.01f));
    }

    [Test]
    public void TableOnFourLegs_StandsLevelOnEachLeg()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var leg = new BoxCollisionShape(0.05f, 0.4f, 0.05f);
        var parts = new List<CompoundPart> { new(new BoxCollisionShape(1f, 0.05f, 0.6f), new LocalPose(new Vector3F(0, 0.85f, 0))) };
        foreach (var corner in new[] { new Vector3F(0.9f, 0.4f, 0.5f), new Vector3F(-0.9f, 0.4f, 0.5f),
                     new Vector3F(0.9f, 0.4f, -0.5f), new Vector3F(-0.9f, 0.4f, -0.5f) })
        {
            parts.Add(new CompoundPart(leg, new LocalPose(corner)));
        }

        var table = scene.World.Add(new BodyDescription
        {
            Shape = new CompoundCollisionShape(parts),
            Position = new WorldPosition(0, 0.1f, 0)
        });

        scene.Run(2f);

        var pose = scene.World.GetPose(table);
        Assert.That(pose.Position.Y, Is.EqualTo(0).Within(0.02f), "it stands on its legs' feet");
        Assert.That(Vector3F.Transform(Vector3F.UnitY, pose.Rotation).Y, Is.GreaterThan(0.9999f), "level");
        Assert.That(scene.World.IsAwake(table), Is.False, "it falls asleep at rest");
    }

    [Test]
    public void HammerOnItsSide_FallsHeadFirst()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var hammer = scene.World.Add(new BodyDescription
        {
            Shape = new CompoundCollisionShape([
                new CompoundPart(new CapsuleCollisionShape(0.5f, 0.03f), new LocalPose(new Vector3F(0, 0.5f, 0))),
                new CompoundPart(new BoxCollisionShape(0.15f, 0.06f, 0.06f), new LocalPose(new Vector3F(0, 1.06f, 0)))
            ]),
            Position = new WorldPosition(0, 0.01f, 0),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, 0.2f)
        });

        scene.Run(3f);

        var center = scene.World.GetCenterOfMass(hammer);
        Assert.That(center.Y, Is.LessThan(0.2f), "the heavy head pulls it over");
    }

    [Test]
    public void PartsOfOneBody_DoNotCollideWithEachOther()
    {
        var scene = new SceneBuilder(new PhysicsSettings { Gravity = Vector3F.Zero });
        var box = new BoxCollisionShape(new Vector3F(0.5f));
        var body = scene.World.Add(new BodyDescription
        {
            Shape = new CompoundCollisionShape([
                new CompoundPart(box, LocalPose.Identity),
                new CompoundPart(box, new LocalPose(new Vector3F(0.3f, 0, 0)))
            ])
        });

        scene.Run(1f);

        var points = new List<ContactPoint>();
        scene.World.GetContactPoints(points);
        Assert.That(points, Is.Empty);
        Assert.That(scene.World.GetLinearVelocity(body).Length(), Is.LessThan(1e-6f));
    }

    [Test]
    public void RayAndOverlap_FindACompoundByItsParts()
    {
        var world = new PhysicsWorld(new PhysicsSettings { Gravity = Vector3F.Zero });
        var sphere = new SphereCollisionShape(0.5f);
        var body = world.Add(new BodyDescription
        {
            Kind = BodyKind.Static,
            Shape = new CompoundCollisionShape([
                new CompoundPart(sphere, new LocalPose(new Vector3F(-2, 0, 0))),
                new CompoundPart(sphere, new LocalPose(new Vector3F(2, 0, 0)))
            ])
        });

        var hitGap = world.RayCast(new WorldPosition(0, 5, 0), -Vector3F.UnitY, 10, out _);
        var hitPart = world.RayCast(new WorldPosition(2, 5, 0), -Vector3F.UnitY, 10, out var hit);
        var found = new List<BodyHandle>();
        world.Overlap(new BoxCollisionShape(3, 1, 1), Pose.Identity, found);

        Assert.That(hitGap, Is.False, "the ray passes between the parts");
        Assert.That(hitPart, Is.True);
        Assert.That(hit.Body, Is.EqualTo(body));
        Assert.That(hit.Distance, Is.EqualTo(4.5f).Within(1e-4f));
        Assert.That(found, Is.EqualTo(new[] { body }), "found once, though both parts overlap");
    }

    [Test]
    public void RemovedCompound_LeavesNoPartBehind()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = new BoxCollisionShape(new Vector3F(0.5f));
        var compound = new CompoundCollisionShape([
            new CompoundPart(box, new LocalPose(new Vector3F(-1, 0, 0))),
            new CompoundPart(box, new LocalPose(new Vector3F(1, 0, 0)))
        ]);
        var first = scene.World.Add(new BodyDescription { Shape = compound, Position = new WorldPosition(0, 0.5f, 0) });
        scene.Run(0.5f);

        scene.World.Remove(first);
        var second = scene.World.Add(new BodyDescription { Shape = compound, Position = new WorldPosition(0, 3f, 0) });
        scene.Run(0.1f);

        var found = new List<BodyHandle>();
        scene.World.Overlap(new BoxCollisionShape(3, 0.6f, 1), new Pose(new WorldPosition(0, 0.5f, 0)), found);
        Assert.That(found, Has.None.EqualTo(first));
        Assert.That(scene.World.RayCast(new WorldPosition(1, 10, 0), -Vector3F.UnitY, 20, out var hit), Is.True);
        Assert.That(hit.Body, Is.EqualTo(second));
    }

    [Test]
    public void SetPose_PlacesTheOriginOfACompound()
    {
        var world = new PhysicsWorld(new PhysicsSettings { Gravity = Vector3F.Zero });
        var body = world.Add(new BodyDescription
        {
            Shape = new CompoundCollisionShape([new CompoundPart(new SphereCollisionShape(0.5f), new LocalPose(new Vector3F(1, 0, 0)))])
        });
        var turn = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, MathF.PI / 2);

        world.SetPose(body, new Pose(new WorldPosition(5, 0, 0), turn));

        var pose = world.GetPose(body);
        Assert.That((pose.Position - new WorldPosition(5, 0, 0)).Length(), Is.LessThan(1e-5f));
        var center = new WorldPosition(5, 0, 0) + Vector3F.Transform(Vector3F.UnitX, turn);
        Assert.That((world.GetCenterOfMass(body) - center).Length(), Is.LessThan(1e-5f));
    }

    [Test]
    public void SpinningCompound_TurnsAroundItsCenterOfMass()
    {
        var world = new PhysicsWorld(new PhysicsSettings { Gravity = Vector3F.Zero });
        var body = world.Add(new BodyDescription
        {
            Shape = new CompoundCollisionShape([new CompoundPart(new SphereCollisionShape(0.5f), new LocalPose(new Vector3F(1, 0, 0)))]),
            AngularVelocity = new Vector3F(0, 1, 0),
            CanSleep = false
        });
        var center = world.GetCenterOfMass(body);

        for (var step = 0; step < world.Settings.StepRate; step++)
        {
            world.Step();
        }

        Assert.That((world.GetCenterOfMass(body) - center).Length(), Is.LessThan(1e-4f));
        Assert.That((world.GetPose(body).Position - center).Length(), Is.EqualTo(1).Within(1e-4f),
            "the origin swings around the center");
    }

    private static bool NearlyEqual(float first, float second) => MathF.Abs(first - second) <= 1e-4f * MathF.Max(1, MathF.Abs(second));

    private static void AssertNear(in Matrix3x3<double> actual, in Matrix3x3<double> expected)
    {
        var actualValues = actual.ToArray();
        var expectedValues = expected.ToArray();
        for (var index = 0; index < actualValues.Length; index++)
        {
            Assert.That(actualValues[index], Is.EqualTo(expectedValues[index]).Within(1e-4));
        }
    }
}
