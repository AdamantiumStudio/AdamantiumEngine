using System;
using System.Collections.Generic;
using System.Diagnostics;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>A simulation's layers: named, which of them collide, a body's and a part's own, and the queries that meet
/// only some.</summary>
[TestFixture]
public class PhysicsLayerTests
{
    private static readonly BoxCollisionShape Crate = new(0.5f, 0.5f, 0.5f);
    private static readonly BoxCollisionShape Half = new(5, 0.5f, 5);

    [Test]
    public void Layers_AreNamedInTurn_AndFoundByName()
    {
        using var simulation = new PhysicsSimulation();
        var layers = simulation.Layers;
        var debris = layers.Define("Debris");

        Assert.Multiple(() =>
        {
            Assert.That(layers.Default.Index, Is.Zero);
            Assert.That(layers.NameOf(layers.Default), Is.EqualTo("Default"));
            Assert.That(debris.Index, Is.EqualTo(1));
            Assert.That(layers["Debris"], Is.EqualTo(debris));
            Assert.That(layers.NameOf(debris), Is.EqualTo("Debris"));
            Assert.That(layers.Collide(debris, layers.Default), Is.True, "every layer collides until told");
            Assert.Throws<ArgumentException>(() => layers.Define("Debris"));
            Assert.Throws<KeyNotFoundException>(() => _ = layers["Water"]);
        });

        for (var index = 2; index < PhysicsLayers.Count; index++)
            layers.Define($"Layer {index}");

        Assert.Throws<InvalidOperationException>(() => layers.Define("One too many"));
    }

    [Test]
    public void ALayerNotDefinedHere_IsRefused()
    {
        using var other = new PhysicsSimulation();
        other.Layers.Define("First");
        var foreign = other.Layers.Define("Second");
        using var simulation = new PhysicsSimulation();
        var crate = simulation.AddBody(new BodySettings { Shape = Crate });

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => simulation.AddBody(new BodySettings { Shape = Crate, Layer = foreign }));
            Assert.Throws<ArgumentException>(() => simulation.SetLayer(crate, foreign));
            Assert.Throws<ArgumentException>(() => simulation.SetParts(crate, [new BodyPart { Shape = Crate, Layer = foreign }]));
            Assert.Throws<ArgumentException>(() => simulation.Layers.SetCollides(foreign, simulation.Layers.Default, false));
            Assert.Throws<ArgumentException>(() => simulation.Layers.NameOf(foreign));
        });
    }

    [Test]
    public void ACrateOnALayerThatDoesNotCollideWithTheFloors_FallsThrough()
    {
        using var simulation = new PhysicsSimulation();
        var ghosts = simulation.Layers.Define("Ghosts");
        simulation.Layers.SetCollides(ghosts, simulation.Layers.Default, false);
        AddFloor(simulation);
        var ghost = AddCrate(simulation, -2, ghosts);
        var crate = AddCrate(simulation, 2, simulation.Layers.Default);

        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.PoseOf(ghost).Position.Y, Is.LessThan(-2), "passed through");
            Assert.That(simulation.PoseOf(crate).Position.Y, Is.EqualTo(0.5).Within(0.05), "held up");
        });
    }

    [Test]
    public void LayersThatStopColliding_LetGoOfACrateResting_AndCatchItOnceTheyCollideAgain()
    {
        using var simulation = new PhysicsSimulation();
        var crates = simulation.Layers.Define("Crates");
        AddFloor(simulation);
        var crate = AddCrate(simulation, 0, crates);
        StepFor(simulation, 1);
        var resting = simulation.PoseOf(crate).Position.Y;

        simulation.Layers.SetCollides(crates, simulation.Layers.Default, false);
        StepFor(simulation, 0.1f);
        var falling = simulation.PoseOf(crate).Position.Y;
        simulation.SetPose(crate, new Pose(new WorldPosition(0, 3, 0)));
        StepFor(simulation, 0.2f);
        simulation.Layers.SetCollides(crates, simulation.Layers.Default, true);
        StepFor(simulation, 2);

        Assert.Multiple(() =>
        {
            Assert.That(resting, Is.EqualTo(0.5).Within(0.05));
            Assert.That(falling, Is.LessThan(resting - 0.02), "let go of at once");
            Assert.That(simulation.PoseOf(crate).Position.Y, Is.EqualTo(0.5).Within(0.05), "caught again");
        });
    }

    // A crate floating still, sunk in the floor it passes through: neither moves, yet once they collide - by their
    // layers, or by its own - it is pushed out.
    [TestCase(false)]
    [TestCase(true)]
    public void BodiesStandingStill_MeetOnceTheyCollide(bool byItsLayer)
    {
        using var simulation = new PhysicsSimulation();
        var ghosts = simulation.Layers.Define("Ghosts");
        simulation.Layers.SetCollides(ghosts, simulation.Layers.Default, false);
        AddFloor(simulation);
        var crate = simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(0, 0.3, 0), Layer = ghosts, GravityScale = 0, CanSleep = false
        });
        StepFor(simulation, 0.5f);
        var sunk = simulation.PoseOf(crate).Position.Y;

        if (byItsLayer)
            simulation.SetLayer(crate, simulation.Layers.Default);
        else
            simulation.Layers.SetCollides(ghosts, simulation.Layers.Default, true);

        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(sunk, Is.EqualTo(0.3).Within(1e-6), "passed through, still");
            Assert.That(simulation.PoseOf(crate).Position.Y, Is.GreaterThan(0.45), "pushed out");
        });
    }

    // A floor of triangles, its pairs held by a piece for each triangle met: put on a ghost layer, it lets go of the
    // crates on it, pieces and all.
    [Test]
    public void AMeshPutOnALayer_LetsGoOfWhatRestsOnIt()
    {
        using var simulation = new PhysicsSimulation();
        var ghosts = simulation.Layers.Define("Ghosts");
        simulation.Layers.SetCollides(ghosts, simulation.Layers.Default, false);
        Vector3F[] corners = [new(-10, 0, -10), new(-10, 0, 10), new(10, 0, 10), new(10, 0, -10)];
        var floor = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = new TriangleMeshCollisionShape(corners, [0, 1, 2, 0, 2, 3])
        });
        BodyId[] crates = [AddCrate(simulation, -2, simulation.Layers.Default), AddCrate(simulation, 0, simulation.Layers.Default),
            AddCrate(simulation, 2, simulation.Layers.Default)];
        StepFor(simulation, 1);
        var resting = simulation.PoseOf(crates[1]).Position.Y;

        simulation.SetLayer(floor, ghosts);
        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(resting, Is.EqualTo(0.5).Within(0.05));
            foreach (var crate in crates)
                Assert.That(simulation.PoseOf(crate).Position.Y, Is.LessThan(-2), "passed through");
        });
    }

    [Test]
    public void ALayersCalls_AreRefusedOnceTheSimulationIsDisposed()
    {
        var simulation = new PhysicsSimulation();
        var layers = simulation.Layers;
        simulation.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => layers.Define("Late"));
            Assert.Throws<ObjectDisposedException>(() => layers.NameOf(layers.Default));
            Assert.Throws<ObjectDisposedException>(() => layers.Collide(layers.Default, layers.Default));
        });
    }

    [Test]
    public void ABodyPutOnALayer_CollidesAsThatLayerDoes()
    {
        using var simulation = new PhysicsSimulation();
        var ghosts = simulation.Layers.Define("Ghosts");
        simulation.Layers.SetCollides(ghosts, simulation.Layers.Default, false);
        AddFloor(simulation);
        var crate = AddCrate(simulation, 0, simulation.Layers.Default);
        StepFor(simulation, 1);
        var asked = simulation.LayerOf(crate);

        simulation.SetLayer(crate, ghosts);
        var beforeTheStep = simulation.LayerOf(crate);
        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(simulation.Layers.Default));
            Assert.That(beforeTheStep, Is.EqualTo(simulation.Layers.Default), "read from the last step");
            Assert.That(simulation.LayerOf(crate), Is.EqualTo(ghosts));
            Assert.That(simulation.PoseOf(crate).Position.Y, Is.LessThan(-2), "passed through");
        });
    }

    // A floor of two halves, one a ghost of its own: what drops on that half falls through it, before the floor's layer
    // changes and after.
    [Test]
    public void APartsOwnLayer_IsKeptWhenTheBodysChanges()
    {
        using var simulation = new PhysicsSimulation();
        var ghosts = simulation.Layers.Define("Ghosts");
        var stone = simulation.Layers.Define("Stone");
        simulation.Layers.SetCollides(ghosts, simulation.Layers.Default, false);
        var floor = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static,
            Layer = stone,
            Parts =
            [
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(-5, -0.5f, 0)), Layer = ghosts },
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(5, -0.5f, 0)) }
            ]
        });
        var overGhost = AddCrate(simulation, -5, simulation.Layers.Default);
        var overStone = AddCrate(simulation, 5, simulation.Layers.Default);
        StepFor(simulation, 1);

        simulation.SetLayer(floor, simulation.Layers.Default);
        var dropped = AddCrate(simulation, -3, simulation.Layers.Default);
        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.PoseOf(overGhost).Position.Y, Is.LessThan(-2), "through the ghost half");
            Assert.That(simulation.PoseOf(overStone).Position.Y, Is.EqualTo(0.5).Within(0.05), "held up by the other");
            Assert.That(simulation.PoseOf(dropped).Position.Y, Is.LessThan(-2), "the half's own layer kept");
        });
    }

    [Test]
    public void Queries_MeetOnlyTheLayersOfTheirFilter()
    {
        using var simulation = new PhysicsSimulation();
        var debris = simulation.Layers.Define("Debris");
        AddFloor(simulation);
        var plain = AddCrate(simulation, -2, simulation.Layers.Default);
        var piece = AddCrate(simulation, 2, debris);
        simulation.Step();
        var onlyDebris = new QueryFilter { Layers = LayerMask.Of(debris) };
        Span<BodyId> found = stackalloc BodyId[8];

        var plainHit = simulation.RayCast(new WorldPosition(-2, 5, 0), -Vector3F.UnitY, 10, out var hitByDefault);
        var plainMissed = !simulation.RayCast(new WorldPosition(-2, 5, 0), -Vector3F.UnitY, 3, out _, onlyDebris);
        var pieceHit = simulation.RayCast(new WorldPosition(2, 5, 0), -Vector3F.UnitY, 10, out var hitOfDebris, onlyDebris);
        var overlapped = simulation.Overlap(new BoxCollisionShape(5, 2, 2), new Pose(new WorldPosition(0, 0.5, 0)), found,
            onlyDebris);
        var firstFound = found[0];
        var cast = simulation.CastShape(Crate, new Pose(new WorldPosition(-6, 0.5, 0)), new Vector3F(12, 0, 0),
            out var shapeHit, onlyDebris);

        Assert.Multiple(() =>
        {
            Assert.That(new QueryFilter().Layers, Is.EqualTo(LayerMask.All), "every layer unless told");
            Assert.That(plainHit && hitByDefault.Body == plain, Is.True);
            Assert.That(plainMissed, Is.True, "the default layer passed by");
            Assert.That(pieceHit && hitOfDebris.Body == piece, Is.True);
            Assert.That(overlapped, Is.EqualTo(1));
            Assert.That(firstFound, Is.EqualTo(piece));
            Assert.That(cast && shapeHit.Body == piece, Is.True, "the crate in its way passed by");
        });
    }

    // While it runs, a query sees the layer the last step read put a body on: not the one asked, until read.
    [Test]
    public void QueriesWhileItRuns_SeeABodysLayerOnceItsStepIsRead()
    {
        using var simulation = new PhysicsSimulation();
        var debris = simulation.Layers.Define("Debris");
        AddFloor(simulation);
        var crate = AddCrate(simulation, 0, simulation.Layers.Default);
        simulation.Step();
        var onlyDebris = new QueryFilter { Layers = LayerMask.Of(debris) };
        simulation.Start();

        simulation.SetLayer(crate, debris);
        var metBeforeRead = simulation.RayCast(new WorldPosition(0, 5, 0), -Vector3F.UnitY, 10, out _, onlyDebris);
        var waited = Stopwatch.StartNew();
        while (simulation.LayerOf(crate) != debris && waited.ElapsedMilliseconds < 5000)
            simulation.Update();

        var metOnceRead = simulation.RayCast(new WorldPosition(0, 5, 0), -Vector3F.UnitY, 10, out var hit, onlyDebris);
        simulation.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(metBeforeRead, Is.False);
            Assert.That(simulation.LayerOf(crate), Is.EqualTo(debris));
            Assert.That(metOnceRead && hit.Body == crate, Is.True);
        });
    }

    [Test]
    public void AMask_HoldsTheLayersPutInIt()
    {
        using var simulation = new PhysicsSimulation();
        var first = simulation.Layers.Define("First");
        var second = simulation.Layers.Define("Second");
        var mask = LayerMask.Of(first).With(second).Without(first);

        Assert.Multiple(() =>
        {
            Assert.That(mask.Contains(second), Is.True);
            Assert.That(mask.Contains(first), Is.False);
            Assert.That(default(LayerMask).Contains(simulation.Layers.Default), Is.False);
            Assert.That(LayerMask.All.Contains(second), Is.True);
        });
    }

    private static void AddFloor(PhysicsSimulation simulation) =>
        simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(10, 0.5f, 5), Position = new WorldPosition(0, -0.5, 0)
        });

    private static BodyId AddCrate(PhysicsSimulation simulation, double x, Layer layer) =>
        simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(x, 0.5, 0), Layer = layer, CanSleep = false
        });

    private static void StepFor(PhysicsSimulation simulation, float seconds)
    {
        for (var step = 0; step < seconds * simulation.Settings.StepRate; step++)
            simulation.Step();
    }
}
