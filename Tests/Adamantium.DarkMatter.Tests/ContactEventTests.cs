using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>The touches a simulation tells, begun and ended, and the triggers that only tell what enters and leaves
/// them.</summary>
[TestFixture]
public class ContactEventTests
{
    private static readonly BoxCollisionShape Crate = new(0.5f, 0.5f, 0.5f);
    private static readonly BoxCollisionShape Zone = new(2, 1, 2);

    [Test]
    public void ACrateDropped_BeginsToTouchTheFloorOnce_AsHardAsItFell()
    {
        using var simulation = new PhysicsSimulation();
        var floor = AddFloor(simulation);
        var crate = AddCrate(simulation, 2);
        var told = new List<ContactEventArgs>();

        StepFor(simulation, 2, told);

        Assert.That(told, Has.Count.EqualTo(1), "it bounces on nothing and keeps touching");
        var touch = told[0];
        Assert.Multiple(() =>
        {
            Assert.That(touch.Change, Is.EqualTo(ContactChange.Began));
            Assert.That(touch.Involves(crate, out var other) && other == floor, Is.True);
            Assert.That(touch.IsTrigger, Is.False);
            Assert.That(touch.Point.Y, Is.EqualTo(0).Within(0.05));
            Assert.That(MathF.Abs(touch.Normal.Y), Is.EqualTo(1).Within(1e-3f));
            // Fallen 1.5 meters, it meets the floor at about 5.4 meters per second.
            Assert.That(touch.ApproachSpeed, Is.EqualTo(5.4f).Within(0.3f));
        });
    }

    [Test]
    public void ACratePutAway_EndsItsTouchOnce()
    {
        using var simulation = new PhysicsSimulation();
        AddFloor(simulation);
        var crate = AddCrate(simulation, 0.5);
        StepFor(simulation, 1);
        var told = new List<ContactEventArgs>();

        simulation.SetPose(crate, new Pose(new WorldPosition(0, 5, 0)));
        StepFor(simulation, 0.1f, told);

        Assert.That(told.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Ended }));
        Assert.That(told[0].Involves(crate, out _), Is.True);
    }

    // A crate shot past another twenty centimeters off: near, never touching, it tells no touch.
    [Test]
    public void ACratePassingNear_TellsNoTouch()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        AddCrate(simulation, 0);
        simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(-10, 0, 1.2), LinearVelocity = new Vector3F(30, 0, 0)
        });
        var told = new List<ContactEventArgs>();

        StepFor(simulation, 1, told);

        Assert.That(told, Is.Empty);
    }

    // Lifted five centimeters, past the gap a touch is met ahead at but within the pair's boxes: the pair stays, its
    // touch ends; put back, it begins again.
    [Test]
    public void ATouchParted_EndsWhileThePairStaysNear_AndBeginsAgain()
    {
        using var simulation = new PhysicsSimulation();
        AddFloor(simulation);
        var crate = simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(0, 0.5, 0), GravityScale = 0, CanSleep = false
        });
        StepFor(simulation, 0.5f);
        var told = new List<ContactEventArgs>();

        simulation.SetPose(crate, new Pose(new WorldPosition(0, 0.55, 0)));
        StepFor(simulation, 0.1f, told);
        var parted = told.ToArray();
        simulation.SetPose(crate, new Pose(new WorldPosition(0, 0.5, 0)));
        StepFor(simulation, 0.1f, told);

        Assert.Multiple(() =>
        {
            Assert.That(parted.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Ended }));
            Assert.That(told.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Ended, ContactChange.Began }));
        });
    }

    [Test]
    public void ABodyTakenOut_EndsItsTouches_AndIsInNoneAfter()
    {
        using var simulation = new PhysicsSimulation();
        AddFloor(simulation);
        var crate = AddCrate(simulation, 0.5);
        var other = AddCrate(simulation, 0.5, x: 3);
        StepFor(simulation, 1);

        simulation.RemoveBody(crate);
        simulation.Step();
        var atRemoval = simulation.Contacts.ToArray();
        var after = new List<ContactEventArgs>();
        StepFor(simulation, 1, after);

        Assert.Multiple(() =>
        {
            Assert.That(atRemoval, Has.Length.EqualTo(1));
            Assert.That(atRemoval[0].Change, Is.EqualTo(ContactChange.Ended));
            Assert.That(atRemoval[0].Involves(crate, out _), Is.True);
            Assert.That(after, Is.Empty, "the other crate rests as it did");
            Assert.That(simulation.Contains(other), Is.True);
        });
    }

    // A cart resting on two feet loses one: its touch ends, told with the foot's own, which the cart no longer has.
    [Test]
    public void APartTakenOff_EndsItsTouch_ToldWithItsOwn()
    {
        using var simulation = new PhysicsSimulation();
        var floor = AddFloor(simulation);
        var cart = simulation.AddBody(new BodySettings
        {
            Parts = (BodyPart[])
            [
                new BodyPart { Shape = Crate, Pose = new LocalPose(new Vector3F(-1, 0, 0)), Data = "left" },
                new BodyPart { Shape = Crate, Pose = new LocalPose(new Vector3F(1, 0, 0)), Data = "right" }
            ],
            Position = new WorldPosition(0, 0.5, 0)
        });
        StepFor(simulation, 0.5f);

        simulation.SetParts(cart, (BodyPart[])[new BodyPart { Shape = Crate, Pose = new LocalPose(new Vector3F(1, 0, 0)), Data = "right" }]);
        simulation.Step();
        var atChange = simulation.Contacts.ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(atChange.Count(touch => touch.Change == ContactChange.Ended), Is.GreaterThanOrEqualTo(1));
            Assert.That(atChange.Any(touch => touch.Change == ContactChange.Ended && touch.Involves(cart, out var other)
                                              && other == floor
                                              && (touch.First == cart ? touch.FirstPartData : touch.SecondPartData)
                                              is "left"), Is.True);
            Assert.That(atChange.All(touch => (touch.First == cart ? touch.FirstPartData : touch.SecondPartData)
                                              is "left" or "right"), Is.True);
        });
    }

    // A crate resting in a zone on the floor, taken out: it leaves the zone and the floor, and the steps go on clean.
    [Test]
    public void ABodyTakenOutInATrigger_LeavesIt()
    {
        using var simulation = new PhysicsSimulation();
        AddFloor(simulation);
        var zone = AddZone(simulation, 1);
        var crate = AddCrate(simulation, 0.5);
        var told = new List<ContactEventArgs>();
        StepFor(simulation, 1, told);
        var entered = told.Count(touch => touch.IsTrigger && touch.Change == ContactChange.Began);

        simulation.RemoveBody(crate);
        simulation.Step();
        var atRemoval = simulation.Contacts.ToArray();
        var after = new List<ContactEventArgs>();
        StepFor(simulation, 0.5f, after);

        Assert.Multiple(() =>
        {
            Assert.That(entered, Is.EqualTo(1));
            Assert.That(atRemoval.Count(touch => touch.IsTrigger && touch.Change == ContactChange.Ended
                                                 && touch.Involves(zone, out var other) && other == crate), Is.EqualTo(1));
            Assert.That(atRemoval.Count(touch => !touch.IsTrigger && touch.Change == ContactChange.Ended), Is.EqualTo(1));
            Assert.That(after, Is.Empty);
        });
    }

    // A kinematic zone swept across a floor of triangles, half sunk in it: it enters the floor and leaves it once, its
    // touches of triangles kept by the pair alone.
    [Test]
    public void ATriggerSweptOverAMesh_EntersAndLeavesItOnce()
    {
        using var simulation = new PhysicsSimulation();
        Vector3F[] corners = [new(-10, 0, -10), new(-10, 0, 10), new(10, 0, 10), new(10, 0, -10)];
        var floor = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = new TriangleMeshCollisionShape(corners, [0, 1, 2, 0, 2, 3])
        });
        simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Kinematic, Position = new WorldPosition(-15, 0, 0), LinearVelocity = new Vector3F(10, 0, 0),
            Parts = [new BodyPart { Shape = Crate, IsTrigger = true }]
        });
        var told = new List<ContactEventArgs>();

        StepFor(simulation, 3, told);

        Assert.Multiple(() =>
        {
            Assert.That(told.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Began, ContactChange.Ended }));
            Assert.That(told.All(touch => touch.IsTrigger && touch.Involves(floor, out _)), Is.True);
        });
    }

    // A crate resting in a zone: the zone's layer and the crate's made not to collide - by the layers, or by the crate
    // put on another - it leaves the zone, and stays on the floor.
    [TestCase(false)]
    [TestCase(true)]
    public void ATriggerAndABodyThatStopColliding_EndTheirTouch(bool byTheBodysLayer)
    {
        using var simulation = new PhysicsSimulation();
        var zones = simulation.Layers.Define("Zones");
        var hidden = simulation.Layers.Define("Hidden");
        simulation.Layers.SetCollides(zones, hidden, false);
        AddFloor(simulation);
        simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Position = new WorldPosition(0, 1, 0), Layer = zones,
            Parts = [new BodyPart { Shape = Zone, IsTrigger = true }]
        });
        var crate = AddCrate(simulation, 0.5);
        StepFor(simulation, 0.5f);

        if (byTheBodysLayer)
            simulation.SetLayer(crate, hidden);
        else
            simulation.Layers.SetCollides(zones, simulation.Layers.Default, false);

        var told = new List<ContactEventArgs>();
        StepFor(simulation, 0.5f, told);

        Assert.Multiple(() =>
        {
            Assert.That(told.Select(touch => (touch.Change, touch.IsTrigger)),
                Is.EqualTo(new[] { (ContactChange.Ended, true) }));
            Assert.That(simulation.PoseOf(crate).Position.Y, Is.EqualTo(0.5).Within(0.05), "on the floor still");
        });
    }

    // A kinematic crate standing in a zone, made static: two static bodies make no pair, so it leaves the zone.
    [Test]
    public void AKinematicBodyMadeStaticInATrigger_LeavesIt()
    {
        using var simulation = new PhysicsSimulation();
        AddZone(simulation, 0);
        var crate = simulation.AddBody(new BodySettings { Kind = BodyKind.Kinematic, Shape = Crate });
        var told = new List<ContactEventArgs>();
        StepFor(simulation, 0.1f, told);

        simulation.SetKind(crate, BodyKind.Static);
        StepFor(simulation, 0.1f, told);

        Assert.That(told.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Began, ContactChange.Ended }));
    }

    // A crate dropped through a zone above the floor: it enters and leaves the zone, unslowed, then lands.
    [Test]
    public void ACrateFallingThroughATrigger_EntersAndLeavesIt_AndIsNotSlowed()
    {
        using var simulation = new PhysicsSimulation();
        var floor = AddFloor(simulation);
        var zone = AddZone(simulation, 4);
        var crate = AddCrate(simulation, 7);
        var told = new List<ContactEventArgs>();

        StepFor(simulation, 3, told);

        Assert.That(told.Select(touch => (touch.Change, touch.IsTrigger)), Is.EqualTo(new[]
        {
            (ContactChange.Began, true), (ContactChange.Ended, true), (ContactChange.Began, false)
        }));
        Assert.Multiple(() =>
        {
            Assert.That(told[0].Involves(zone, out var entered) && entered == crate, Is.True);
            Assert.That(told[0].ApproachSpeed, Is.Zero, "a trigger meets nothing hard");
            Assert.That(told[2].Involves(floor, out _), Is.True);
            Assert.That(simulation.PoseOf(crate).Position.Y, Is.EqualTo(0.5).Within(0.05), "landed");
        });
    }

    // A zone sunk in the floor, its top a centimeter under the floor's: a crate resting on the floor is near it, not in it.
    [Test]
    public void ATrigger_IsEnteredOnlyWhereItIsOverlapped()
    {
        using var simulation = new PhysicsSimulation();
        AddFloor(simulation);
        AddZone(simulation, -1.01);
        AddCrate(simulation, 0.5);
        var told = new List<ContactEventArgs>();

        StepFor(simulation, 1, told);

        Assert.That(told.Where(touch => touch.IsTrigger), Is.Empty);
    }

    [Test]
    public void ATrigger_DoesNotSlowWhatPassesThroughIt()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        AddZone(simulation, 0);
        var crate = simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(-5, 0, 0), LinearVelocity = new Vector3F(5, 0, 0)
        });

        StepFor(simulation, 2);

        Assert.That(simulation.LinearVelocityOf(crate).X, Is.EqualTo(5).Within(1e-4f));
    }

    // A zone swept over a crate asleep on the floor: told of it, the crate sleeps on.
    [Test]
    public void ATriggerPassingASleeper_TellsOfItAndLetsItSleep()
    {
        using var simulation = new PhysicsSimulation();
        AddFloor(simulation);
        var crate = AddCrate(simulation, 0.5);
        StepFor(simulation, 3);
        var asleep = simulation.IsSleeping(crate);
        var zone = simulation.AddBody(new BodySettings
        {
            // Clear of the floor, which it would tell of too.
            Kind = BodyKind.Kinematic, Position = new WorldPosition(-6, 1.05, 0), LinearVelocity = new Vector3F(6, 0, 0),
            Parts = [new BodyPart { Shape = Zone, IsTrigger = true }]
        });
        var told = new List<ContactEventArgs>();
        var sleptThrough = true;

        for (var step = 0; step < 2 * simulation.Settings.StepRate; step++)
        {
            simulation.Step();
            told.AddRange(simulation.Contacts.ToArray());
            sleptThrough &= simulation.IsSleeping(crate);
        }

        Assert.Multiple(() =>
        {
            Assert.That(asleep, Is.True);
            Assert.That(told.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Began, ContactChange.Ended }));
            Assert.That(told.All(touch => touch.IsTrigger && touch.Involves(zone, out var other) && other == crate),
                Is.True);
            Assert.That(sleptThrough, Is.True, "not woken at any step");
        });
    }

    [Test]
    public void ATriggerPart_WeighsNothing()
    {
        using var simulation = new PhysicsSimulation();
        var plain = simulation.AddBody(new BodySettings { Parts = [new BodyPart { Shape = Crate }] });
        var reaching = simulation.AddBody(new BodySettings
        {
            Position = new WorldPosition(10, 0, 0),
            Parts =
            [
                new BodyPart { Shape = Crate },
                new BodyPart { Shape = new SphereCollisionShape(3), Pose = new LocalPose(new Vector3F(2, 0, 0)), IsTrigger = true }
            ]
        });

        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.MassOf(reaching), Is.EqualTo(simulation.MassOf(plain)).Within(1e-3f));
            Assert.That(simulation.CenterOfMassOf(reaching).X, Is.EqualTo(10).Within(1e-4), "the crate's own center");
        });
    }

    [Test]
    public void Queries_PassTriggersBy_UnlessTheyMeetThem()
    {
        using var simulation = new PhysicsSimulation();
        var zone = AddZone(simulation, 4);
        simulation.Step();

        var passed = !simulation.RayCast(new WorldPosition(0, 10, 0), -Vector3F.UnitY, 20, out _);
        var met = simulation.RayCast(new WorldPosition(0, 10, 0), -Vector3F.UnitY, 20, out var hit,
            new QueryFilter { MeetsTriggers = true });

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.True);
            Assert.That(met && hit.Body == zone, Is.True);
        });
    }

    [Test]
    public void TwoTriggers_TellNothingOfEachOther()
    {
        using var simulation = new PhysicsSimulation();
        AddZone(simulation, 4);
        simulation.AddBody(new BodySettings
        {
            Position = new WorldPosition(0, 7, 0), Parts = [new BodyPart { Shape = Crate, IsTrigger = true }]
        });
        var told = new List<ContactEventArgs>();

        StepFor(simulation, 1, told);

        Assert.That(told, Is.Empty);
    }

    // A floor of triangles touches a crate as one pair, however many of its triangles the crate stands on.
    [Test]
    public void AMeshFloor_IsTouchedOnce_AndLetGoOfOnce()
    {
        using var simulation = new PhysicsSimulation();
        Vector3F[] corners = [new(-10, 0, -10), new(-10, 0, 10), new(10, 0, 10), new(10, 0, -10)];
        simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = new TriangleMeshCollisionShape(corners, [0, 1, 2, 0, 2, 3])
        });
        var crate = AddCrate(simulation, 1);
        var told = new List<ContactEventArgs>();
        StepFor(simulation, 1, told);

        simulation.SetPose(crate, new Pose(new WorldPosition(0, 5, 0)));
        StepFor(simulation, 0.1f, told);

        Assert.That(told.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Began, ContactChange.Ended }));
    }

    // On its own thread: each Update tells the touches of the steps it read, once.
    [Test]
    public void WhileItRuns_EachUpdateTellsTheTouchesOfTheStepsItRead()
    {
        using var simulation = new PhysicsSimulation();
        AddFloor(simulation);
        AddCrate(simulation, 2);
        var told = new List<ContactEventArgs>();
        simulation.Start();

        var running = Stopwatch.StartNew();
        while (running.ElapsedMilliseconds < 1500)
        {
            simulation.Update();
            told.AddRange(simulation.Contacts.ToArray());
        }

        simulation.Stop();

        Assert.That(told.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Began }));
    }

    [Test]
    public void AKinematicBody_EntersAndLeavesATrigger()
    {
        using var simulation = new PhysicsSimulation();
        var zone = AddZone(simulation, 0);
        var mover = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Kinematic, Shape = Crate, Position = new WorldPosition(-6, 0, 0),
            LinearVelocity = new Vector3F(6, 0, 0)
        });
        var told = new List<ContactEventArgs>();

        StepFor(simulation, 2, told);

        Assert.Multiple(() =>
        {
            Assert.That(told.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Began, ContactChange.Ended }));
            Assert.That(told.All(touch => touch.IsTrigger && touch.Involves(zone, out var other) && other == mover),
                Is.True);
        });
    }

    // Its thread stopped with runs unread: Stop reads them, and tells their touches.
    [Test]
    public void Stopped_ItTellsTheTouchesOfTheRunsItRead()
    {
        using var simulation = new PhysicsSimulation();
        AddFloor(simulation);
        AddCrate(simulation, 0.6);
        simulation.Step();
        simulation.Start();
        Thread.Sleep(1000);

        simulation.Stop();
        var atStop = simulation.Contacts.ToArray();
        simulation.Step();
        var afterStep = simulation.Contacts.Length;

        // Read by an Update before it stops, a touch is not told again by Stop.
        using var read = new PhysicsSimulation();
        AddFloor(read);
        AddCrate(read, 0.6);
        read.Step();
        read.Start();
        Thread.Sleep(1000);
        read.Update();
        var atUpdate = read.Contacts.ToArray();
        read.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(atStop.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Began }));
            Assert.That(afterStep, Is.Zero, "told once");
            Assert.That(atUpdate.Select(touch => touch.Change), Is.EqualTo(new[] { ContactChange.Began }));
            Assert.That(read.Contacts.IsEmpty, Is.True, "not told again");
        });
    }

    // Taken out while the runs that landed it lay unread: neither its landing nor its end is told.
    [Test]
    public void WhileItRuns_ABodyTakenOutBeforeItsTouchIsRead_IsInNone()
    {
        using var simulation = new PhysicsSimulation();
        AddFloor(simulation);
        var crate = AddCrate(simulation, 0.6);
        simulation.Step();
        var addedTouching = simulation.Contacts.Length;
        simulation.Start();
        Thread.Sleep(1000);

        simulation.RemoveBody(crate);
        var told = new List<ContactEventArgs>();
        var running = Stopwatch.StartNew();
        while (running.ElapsedMilliseconds < 500)
        {
            simulation.Update();
            told.AddRange(simulation.Contacts.ToArray());
        }

        simulation.Stop();
        told.AddRange(simulation.Contacts.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(addedTouching, Is.Zero, "added in the air");
            Assert.That(told, Is.Empty);
        });
    }

    private static BodyId AddFloor(PhysicsSimulation simulation) =>
        simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(10, 0.5f, 10), Position = new WorldPosition(0, -0.5, 0)
        });

    private static BodyId AddZone(PhysicsSimulation simulation, double y) =>
        simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Position = new WorldPosition(0, y, 0),
            Parts = [new BodyPart { Shape = Zone, IsTrigger = true }]
        });

    private static BodyId AddCrate(PhysicsSimulation simulation, double y, double x = 0) =>
        simulation.AddBody(new BodySettings { Shape = Crate, Position = new WorldPosition(x, y, 0) });

    private static void StepFor(PhysicsSimulation simulation, float seconds, List<ContactEventArgs> told = null)
    {
        for (var step = 0; step < seconds * simulation.Settings.StepRate; step++)
        {
            simulation.Step();
            told?.AddRange(simulation.Contacts.ToArray());
        }
    }
}
