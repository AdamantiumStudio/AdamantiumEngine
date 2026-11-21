using System;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>The chains over a simulation: a body's actions and readings, bodies and joints told link by link, queries,
/// and the world's own actions - each link the simulation's own method.</summary>
[TestFixture]
public class ChainTests
{
    private static readonly BoxCollisionShape Crate = new(0.5f, 0.5f, 0.5f);

    // The same crate pushed by the chain and by the methods: they move alike, step for step.
    [Test]
    public void ABodysChain_DoesWhatItsMethodsDo()
    {
        using var byChain = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        using var byMethods = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var chained = byChain.AddBody(new BodySettings { Shape = Crate });
        var called = byMethods.AddBody(new BodySettings { Shape = Crate });

        byChain.Body(chained)
            .SetLinearVelocity(new Vector3F(2, 0, 0))
            .ApplyImpulse(new Vector3F(0, 300, 0))
            .SetForce(new Vector3F(0, 0, 50))
            .SetAngularVelocity(new Vector3F(0, 1, 0));
        byMethods.SetLinearVelocity(called, new Vector3F(2, 0, 0));
        byMethods.ApplyImpulse(called, new Vector3F(0, 300, 0));
        byMethods.SetForce(called, new Vector3F(0, 0, 50));
        byMethods.SetAngularVelocity(called, new Vector3F(0, 1, 0));
        for (var step = 0; step < 60; step++)
        {
            byChain.Step();
            byMethods.Step();
        }

        var body = byChain.Body(chained);
        var (pose, linear, angular, mass, kind, status) =
            (body.Pose, body.LinearVelocity, body.AngularVelocity, body.Mass, body.Kind, body.Status);
        Assert.Multiple(() =>
        {
            Assert.That(pose, Is.EqualTo(byMethods.PoseOf(called)));
            Assert.That(linear, Is.EqualTo(byMethods.LinearVelocityOf(called)));
            Assert.That(angular, Is.EqualTo(byMethods.AngularVelocityOf(called)));
            Assert.That(mass, Is.EqualTo(1000).Within(1e-2f));
            Assert.That(kind, Is.EqualTo(BodyKind.Dynamic));
            Assert.That(status, Is.EqualTo(PhysicsStatus.Active));
        });
    }

    [Test]
    public void ABodyToldLinkByLink_IsAddedAsToldInSettings()
    {
        using var simulation = new PhysicsSimulation();
        var crates = simulation.Layers.Define("Crates");
        var ice = new PhysicsMaterial { Friction = 0, FrictionCombine = CombineRule.Minimum };

        var crate = simulation.NewBody()
            .Kinematic()
            .WithShape(Crate)
            .At(new WorldPosition(1, 2, 3))
            .Moving(new Vector3F(0, 0, 4))
            .WithMaterial(ice)
            .OnLayer(crates)
            .WithData("crate")
            .Add();
        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.KindOf(crate), Is.EqualTo(BodyKind.Kinematic));
            Assert.That(simulation.LayerOf(crate), Is.EqualTo(crates));
            Assert.That(simulation.DataOf(crate), Is.EqualTo("crate"));
            Assert.That(simulation.LinearVelocityOf(crate), Is.EqualTo(new Vector3F(0, 0, 4)));
            Assert.That(simulation.PoseOf(crate).Position.Z, Is.EqualTo(3 + 4 * simulation.StepSeconds).Within(1e-6));
        });
    }

    // Two bodies told from one builder: a link of one leaves the builder, and the other, as they were.
    [Test]
    public void ABuildersLinks_LeaveTheBuilderAsItWas()
    {
        using var simulation = new PhysicsSimulation();
        var crate = simulation.NewBody().WithShape(Crate).WithMass(20);
        var wall = crate.Static().At(new WorldPosition(5, 0, 0)).Add();
        var box = crate.At(new WorldPosition(-5, 0, 0)).Add();
        var joint = simulation.NewJoint().Between(box).At(new WorldPosition(-5, 0.5, 0));
        var rod = joint.Distance().To(new WorldPosition(-5, 3.5, 0)).Add();
        var ball = joint.Add();
        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.KindOf(wall), Is.EqualTo(BodyKind.Static));
            Assert.That(simulation.KindOf(box), Is.EqualTo(BodyKind.Dynamic), "the other branch not made static");
            Assert.That(simulation.MassOf(box), Is.EqualTo(20).Within(1e-4f));
            Assert.That(simulation.PositionOf(rod), Is.EqualTo(3).Within(0.05f), "a rod three meters long");
            Assert.That(simulation.PositionOf(ball), Is.Zero, "a ball, not made a rod by the other branch");
        });
    }

    [Test]
    public void AJointToldLinkByLink_HoldsAsToldInSettings()
    {
        using var simulation = new PhysicsSimulation();
        var frame = simulation.NewBody().Static().WithShape(Crate).Add();
        var door = simulation.NewBody().WithShape(Crate).At(new WorldPosition(1, 0, 0)).Add();

        var hinge = simulation.NewJoint()
            .Hinge()
            .Between(door, frame)
            .At(new WorldPosition(0.5, 0, 0))
            .Along(Vector3F.UnitY)
            .WithLimits(new JointLimits(0, MathF.PI / 2))
            .WithMotor(new JointMotor(1.5f, 2000))
            .Add();
        // Turning at 1.5 radians a second, past a second to reach the limit.
        for (var step = 0; step < 2 * simulation.Settings.StepRate; step++)
            simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.StatusOf(hinge), Is.EqualTo(PhysicsStatus.Active));
            Assert.That(simulation.PositionOf(hinge), Is.EqualTo(MathF.PI / 2).Within(0.05f), "opened to its limit");
        });
    }

    [Test]
    public void AQueryToldLinkByLink_MeetsWhatItsFilterDoes()
    {
        using var simulation = new PhysicsSimulation();
        var debris = simulation.Layers.Define("Debris");
        var plain = simulation.NewBody().Static().WithShape(Crate).Add();
        var piece = simulation.NewBody().Static().WithShape(Crate).At(new WorldPosition(0, -2, 0)).OnLayer(debris).Add();
        simulation.Step();
        var from = new WorldPosition(0, 5, 0);

        var metFirst = simulation.Query().RayCast(from, -Vector3F.UnitY, 20, out var first);
        var metDebris = simulation.Query().OnLayers(LayerMask.Of(debris)).RayCast(from, -Vector3F.UnitY, 20, out var onDebris);
        var metPast = simulation.Query().Except(plain).RayCast(from, -Vector3F.UnitY, 20, out var past);
        var metOnBoth = simulation.Query().OnLayers(LayerMask.Of(debris)).OnLayers(LayerMask.Of(simulation.Layers.Default))
            .RayCast(from, -Vector3F.UnitY, 20, out _);

        Assert.Multiple(() =>
        {
            Assert.That(metFirst && first.Body == plain, Is.True);
            Assert.That(metDebris && onDebris.Body == piece, Is.True);
            Assert.That(metPast && past.Body == piece, Is.True);
            Assert.That(metOnBoth, Is.False, "on both: on none of the two");
        });
    }

    [Test]
    public void TheWorldsChain_DoesItsActionsInOrder()
    {
        using var simulation = new PhysicsSimulation();
        var ghosts = simulation.Layers.Define("Ghosts");
        simulation.NewBody().Static().WithShape(new BoxCollisionShape(10, 0.5f, 10)).At(new WorldPosition(0, -0.5, 0)).Add();
        var ghost = simulation.NewBody().WithShape(Crate).At(new WorldPosition(0, 0.5, 0)).OnLayer(ghosts).Add();

        simulation.World()
            .SetGravity(new Vector3F(0, -3.71f, 0))
            .SetCollides(ghosts, simulation.Layers.Default, false)
            .Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.Gravity, Is.EqualTo(new Vector3F(0, -3.71f, 0)));
            Assert.That(simulation.Layers.Collide(ghosts, simulation.Layers.Default), Is.False);
            Assert.That(simulation.LinearVelocityOf(ghost).Y, Is.EqualTo(-3.71f * simulation.StepSeconds).Within(1e-4f),
                "falls through, at Mars' pull");
        });
    }

    [Test]
    public void WakingAnArea_WakesOnlyTheSleepersInIt()
    {
        using var simulation = new PhysicsSimulation();
        simulation.NewBody().Static().WithShape(new BoxCollisionShape(20, 0.5f, 5)).At(new WorldPosition(0, -0.5, 0)).Add();
        var inside = simulation.NewBody().WithShape(Crate).At(new WorldPosition(-5, 0.5, 0)).Add();
        var outside = simulation.NewBody().WithShape(Crate).At(new WorldPosition(5, 0.5, 0)).Add();
        for (var step = 0; step < 360 && !(simulation.IsSleeping(inside) && simulation.IsSleeping(outside)); step++)
            simulation.Step();

        var asleep = simulation.IsSleeping(inside) && simulation.IsSleeping(outside);
        simulation.World().WakeUp(new BoundingBox<double>(new Vector3<double>(-8, -1, -2), new Vector3<double>(-2, 2, 2)))
            .Step();

        Assert.Multiple(() =>
        {
            Assert.That(asleep, Is.True);
            Assert.That(simulation.IsSleeping(inside), Is.False);
            Assert.That(simulation.IsSleeping(outside), Is.True);
        });
    }

    // Crates around a blast: the near one flies harder than the far, away from the center; one past the radius and
    // a static one stay as they were.
    [Test]
    public void ARadialImpulse_PushesTheDynamicBodiesNearAwayFadingWithDistance()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var near = simulation.NewBody().WithShape(Crate).At(new WorldPosition(2, 0, 0)).Add();
        var far = simulation.NewBody().WithShape(Crate).At(new WorldPosition(0, 0, -6)).Add();
        var past = simulation.NewBody().WithShape(Crate).At(new WorldPosition(0, 11, 0)).Add();
        var wall = simulation.NewBody().Static().WithShape(Crate).At(new WorldPosition(-3, 0, 0)).Add();
        simulation.Step();

        simulation.World().ApplyRadialImpulse(WorldPosition.Zero, radius: 10, strength: 1000).Step();

        Assert.Multiple(() =>
        {
            // A thousand kilograms struck with 1000 * (1 - d / 10) newton seconds.
            Assert.That(simulation.LinearVelocityOf(near).X, Is.EqualTo(0.8f).Within(1e-3f));
            Assert.That(simulation.LinearVelocityOf(far).Z, Is.EqualTo(-0.4f).Within(1e-3f));
            Assert.That(simulation.LinearVelocityOf(past), Is.EqualTo(Vector3F.Zero));
            Assert.That(simulation.PoseOf(wall).Position, Is.EqualTo(new WorldPosition(-3, 0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.ApplyRadialImpulse(WorldPosition.Zero, 0, 1));
        });
    }
}
