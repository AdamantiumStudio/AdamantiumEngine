using System;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>How two materials meet, and the material of a body's parts: their own, or the body's.</summary>
[TestFixture]
public class PhysicsMaterialTests
{
    private static readonly BoxCollisionShape Crate = new(0.5f, 0.5f, 0.5f);
    private static readonly BoxCollisionShape Half = new(5, 0.5f, 5);
    private static readonly PhysicsMaterial Ice = new() { Friction = 0, FrictionCombine = CombineRule.Minimum };
    private static readonly PhysicsMaterial Rubber = new() { Friction = 1, FrictionCombine = CombineRule.Maximum };

    [TestCase(CombineRule.Average, CombineRule.Average, 0.5f)]
    [TestCase(CombineRule.Minimum, CombineRule.Average, 0.2f)]
    [TestCase(CombineRule.Multiply, CombineRule.Minimum, 0.16f)]
    [TestCase(CombineRule.Maximum, CombineRule.Multiply, 0.8f)]
    [TestCase(CombineRule.Average, CombineRule.Maximum, 0.8f)]
    public void TwoMaterials_MeetByTheLaterOfTheirRules(CombineRule first, CombineRule second, float expected)
    {
        var low = new PhysicsMaterial { Friction = 0.2f, Restitution = 0.2f, FrictionCombine = first, RestitutionCombine = first };
        var high = new PhysicsMaterial { Friction = 0.8f, Restitution = 0.8f, FrictionCombine = second, RestitutionCombine = second };

        Assert.Multiple(() =>
        {
            Assert.That(PhysicsMaterial.CombineFriction(low, high), Is.EqualTo(expected).Within(1e-6f));
            Assert.That(PhysicsMaterial.CombineRestitution(high, low), Is.EqualTo(expected).Within(1e-6f));
        });
    }

    // Two crates slid over a floor of two halves: the half of ice keeps its own material, the other has the floor's.
    [Test]
    public void APartsOwnMaterial_IsWhatItsContactsMeetBy()
    {
        using var simulation = new PhysicsSimulation();
        simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static,
            Parts =
            [
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(-5, -0.5f, 0)), Material = Ice },
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(5, -0.5f, 0)) }
            ]
        });
        var onIce = AddSlidingCrate(simulation, -5);
        var onFloor = AddSlidingCrate(simulation, 5);

        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.LinearVelocityOf(onIce).X, Is.EqualTo(2).Within(0.05f), "nothing rubs on ice");
            Assert.That(simulation.LinearVelocityOf(onFloor).X, Is.LessThan(0.05f), "stopped by the floor's");
        });
    }

    // The floor turned to ice under crates sliding on it: its half with no material of its own goes with it at once,
    // the half of rubber stays rubber.
    [Test]
    public void TheBodysMaterialSet_ReachesItsPartsWithNoneOfTheirOwn()
    {
        using var simulation = new PhysicsSimulation();
        var floor = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static,
            Parts =
            [
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(-5, -0.5f, 0)), Material = Rubber },
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(5, -0.5f, 0)) }
            ]
        });
        var onRubber = AddSlidingCrate(simulation, -5, speed: 4);
        var onFloor = AddSlidingCrate(simulation, 5, speed: 4);
        StepFor(simulation, 0.25f);
        var sliding = simulation.LinearVelocityOf(onFloor).X;

        simulation.SetMaterial(floor, Ice);
        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(sliding, Is.InRange(0.5f, 3.5f), "slowed by the floor's own, still sliding");
            Assert.That(simulation.LinearVelocityOf(onRubber).X, Is.LessThan(0.05f), "its own kept");
            Assert.That(simulation.LinearVelocityOf(onFloor).X, Is.EqualTo(sliding).Within(0.05f), "the floor's taken");
        });
    }

    // A floor of one shape given parts, one of ice: its contacts meet by the parts' materials from then on.
    [Test]
    public void PartsGivenWithAMaterial_AreMetByIt()
    {
        using var simulation = new PhysicsSimulation();
        var floor = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(10, 0.5f, 5), Position = new WorldPosition(0, -0.5, 0)
        });
        simulation.SetParts(floor,
        [
            new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(-5, 0, 0)), Material = Ice },
            new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(5, 0, 0)) }
        ]);
        var onIce = AddSlidingCrate(simulation, -5);
        var onFloor = AddSlidingCrate(simulation, 5);

        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.LinearVelocityOf(onIce).X, Is.EqualTo(2).Within(0.05f));
            Assert.That(simulation.LinearVelocityOf(onFloor).X, Is.LessThan(0.05f));
        });
    }

    [Test]
    public void AMaterialItCannotUse_IsRefusedWhenAsked()
    {
        using var simulation = new PhysicsSimulation();
        var crate = simulation.AddBody(new BodySettings { Shape = Crate });

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                simulation.SetMaterial(crate, new PhysicsMaterial { FrictionCombine = (CombineRule)9 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetParts(crate,
                [new BodyPart { Shape = Crate, Material = new PhysicsMaterial { Restitution = -1 } }]));
        });
    }

    private static BodyId AddSlidingCrate(PhysicsSimulation simulation, double x, float speed = 2) =>
        simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(x, 0.5, 0), LinearVelocity = new Vector3F(speed, 0, 0),
            CanSleep = false
        });

    private static void StepFor(PhysicsSimulation simulation, float seconds)
    {
        for (var step = 0; step < seconds * simulation.Settings.StepRate; step++)
            simulation.Step();
    }
}
