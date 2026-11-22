using System;
using System.Diagnostics;
using System.Threading;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>The simulation stepping on its own thread, read at each Update; advanced by time; and what a body's
/// settings and a joint on the bodies do.</summary>
[TestFixture]
public class PhysicsSimulationThreadTests
{
    private static readonly BoxCollisionShape Crate = new(0.5f, 0.5f, 0.5f);

    [Test]
    public void Started_ItStepsOnItsOwn_AndUpdateReadsWhatItDid()
    {
        using var simulation = new PhysicsSimulation();
        var body = simulation.AddBody(new BodySettings { Shape = Crate, Position = new WorldPosition(0, 10, 0) });

        simulation.Start();
        var fell = UpdateUntil(simulation, () => simulation.PoseOf(body).Position.Y < 9);
        var drawn = simulation.InterpolatedPoseOf(body).Position.Y;
        var state = simulation.State;
        simulation.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(fell, Is.True, "it fell while the caller only updated");
            Assert.That(state, Is.EqualTo(SimulationState.Running));
            Assert.That(simulation.StatusOf(body), Is.EqualTo(PhysicsStatus.Active));
            Assert.That(simulation.StepNumber, Is.Positive);
            Assert.That(drawn, Is.LessThan(10), "drawn between its last two steps");
            Assert.That(simulation.State, Is.EqualTo(SimulationState.Stopped));
        });
    }

    [Test]
    public void AnActionAskedWhileItRuns_IsDoneOnItsThread()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var body = simulation.AddBody(new BodySettings { Shape = Crate, Mass = 1 });
        simulation.Start();
        UpdateUntil(simulation, () => simulation.StatusOf(body) == PhysicsStatus.Active);

        simulation.ApplyImpulse(body, new Vector3F(3, 0, 0));
        var struck = UpdateUntil(simulation, () => simulation.LinearVelocityOf(body).X > 2.9f);
        simulation.Stop();

        Assert.That(struck, Is.True);
    }

    [Test]
    public void Paused_NothingMoves_AndStartedAgainItGoesOn()
    {
        using var simulation = new PhysicsSimulation();
        var body = simulation.AddBody(new BodySettings { Shape = Crate, Position = new WorldPosition(0, 100, 0) });
        simulation.Start();
        UpdateUntil(simulation, () => simulation.StepNumber > 5);

        simulation.Pause();
        simulation.Update();
        var heldAt = simulation.StepNumber;
        Thread.Sleep(100);
        simulation.Update();
        var stillAt = simulation.StepNumber;
        var drawnAt = simulation.InterpolatedPoseOf(body).Position;
        var standsAt = simulation.PoseOf(body).Position;
        simulation.Start();
        var wentOn = UpdateUntil(simulation, () => simulation.StepNumber > stillAt + 5);
        simulation.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(stillAt, Is.EqualTo(heldAt), "no step while paused");
            Assert.That(drawnAt, Is.EqualTo(standsAt), "drawn where it stands");
            Assert.That(wentOn, Is.True);
        });
    }

    // Stopped, it steps by hand again: what was asked and not done yet waits for the next step; stepped by hand while
    // it runs, it refuses.
    [Test]
    public void Stopped_WhatWasAskedWaits_ForTheNextStepByHand()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var body = simulation.AddBody(new BodySettings { Shape = Crate, Mass = 1 });
        simulation.Start();
        UpdateUntil(simulation, () => simulation.StatusOf(body) == PhysicsStatus.Active);
        var refused = Assert.Throws<InvalidOperationException>(() => simulation.Step());

        simulation.SetLinearVelocity(body, new Vector3F(0, 0, 4));
        simulation.Stop();
        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.Not.Null);
            Assert.That(simulation.LinearVelocityOf(body).Z, Is.EqualTo(4).Within(1e-4f));
        });
    }

    // Put by hand while its thread runs, a body is drawn where it was put at once, never back on its old track, and as
    // it moves on from there once that step is read.
    [Test]
    public void ABodyPutByHandWhileItRuns_IsDrawnThereAtOnce_AndMovesOnFromThere()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var body = simulation.AddBody(new BodySettings
        {
            Shape = Crate, Mass = 1, LinearVelocity = new Vector3F(1, 0, 0), CanSleep = false
        });
        simulation.Start();
        UpdateUntil(simulation, () => simulation.PoseOf(body).Position.X > 0.5);

        simulation.SetPose(body, new Pose(new WorldPosition(0, 50, 0)));
        var drawnAtOnce = simulation.InterpolatedPoseOf(body).Position;
        var lowest = double.MaxValue;
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(2) && simulation.PoseOf(body).Position.Y < 49)
        {
            simulation.Update();
            lowest = Math.Min(lowest, simulation.InterpolatedPoseOf(body).Position.Y);
            Thread.Sleep(2);
        }

        var movedOn = UpdateUntil(simulation, () => simulation.InterpolatedPoseOf(body).Position.X > 0.1);
        var drawn = simulation.InterpolatedPoseOf(body).Position;
        simulation.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(drawnAtOnce, Is.EqualTo(new WorldPosition(0, 50, 0)));
            Assert.That(lowest, Is.EqualTo(50).Within(1e-6), "never drawn back on its old track");
            Assert.That(movedOn, Is.True);
            Assert.That(drawn.Y, Is.EqualTo(50).Within(1e-6), "moving on from where it was put, not from where it was");
        });
    }

    // While it runs, queries meet the bodies where the caller read them, without waiting for its thread.
    [Test]
    public void WhileItRuns_QueriesMeetTheBodiesWhereTheyWereRead()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var crate = simulation.AddBody(new BodySettings
        {
            Shape = Crate, Mass = 1, LinearVelocity = new Vector3F(5, 0, 0), CanSleep = false
        });
        simulation.Start();
        UpdateUntil(simulation, () => simulation.PoseOf(crate).Position.X > 2);
        simulation.Pause();
        simulation.Update();
        var at = simulation.PoseOf(crate).Position;

        var met = simulation.RayCast(at + new Vector3F(0, 10, 0), -Vector3F.UnitY, 20, out var hit);
        var missed = simulation.RayCast(new WorldPosition(0, 10, 0), -Vector3F.UnitY, 20, out _);
        var passed = simulation.RayCast(at + new Vector3F(0, 10, 0), -Vector3F.UnitY, 20, out _,
            new QueryFilter { Except = crate });
        simulation.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Body, Is.EqualTo(crate));
            Assert.That(hit.Point.Y, Is.EqualTo(at.Y + 0.5).Within(1e-3));
            Assert.That(missed, Is.False, "it is no longer where it started");
            Assert.That(passed, Is.False);
        });
    }

    [Test]
    public void ABodyRefusedWhileItRuns_IsToldAtUpdate()
    {
        using var simulation = new PhysicsSimulation();
        var floor = new TriangleMeshCollisionShape([new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)], [0, 1, 2]);
        simulation.Start();
        var body = simulation.AddBody(new BodySettings { Shape = floor });

        var told = UpdateUntil(simulation, () => simulation.StatusOf(body) == PhysicsStatus.Refused);
        simulation.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(told, Is.True);
            Assert.That(simulation.RefusalOf(body), Does.Contain("mesh"));
        });
    }

    // Advanced by time, it takes the steps due and draws between its last two by the time left over.
    [Test]
    public void Advanced_ItTakesTheStepsDue_AndDrawsByTheTimeLeft()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var body = simulation.AddBody(new BodySettings
        {
            Shape = Crate, Mass = 1, LinearVelocity = new Vector3F(1, 0, 0), CanSleep = false
        });
        var step = simulation.StepSeconds;

        var none = simulation.Advance(step * 0.5);
        var one = simulation.Advance(step * 0.75);
        var last = simulation.PoseOf(body).Position.X;
        var drawn = simulation.InterpolatedPoseOf(body).Position.X;
        var three = simulation.Advance(step * 3);

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.Zero);
            Assert.That(one, Is.EqualTo(1));
            Assert.That(three, Is.EqualTo(3));
            Assert.That(last, Is.EqualTo(step).Within(1e-5), "one step at one meter a second");
            Assert.That(drawn, Is.LessThan(last), "a quarter of a step past it, drawn short of it");
            Assert.That(simulation.StepNumber, Is.EqualTo(4));
        });
    }

    // A joint given on the bodies is put where they stand when it is taken: a rod from a point on a turned body.
    [Test]
    public void AJointOnTheBodies_IsPutWhereTheyStandWhenTaken()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var turned = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, MathF.PI / 2);
        var body = simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(0, 0, 5), Rotation = turned
        });
        var rod = simulation.AddJoint(new JointSettings
        {
            Kind = JointKind.Distance, First = body, Space = JointSpace.Bodies, FirstAnchor = new Vector3F(1, 0, 0),
            SecondPoint = new WorldPosition(0, 0, 0)
        });

        simulation.Step();

        // Its own X turned a quarter about Y is the world's -Z: the end at (0, 0, 4), four meters from the origin.
        Assert.That(simulation.PositionOf(rod), Is.EqualTo(4).Within(1e-3f));
    }

    [Test]
    public void ABodysSettings_AreSetAsAsked()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { TimeToSleep = 0.1f });
        var weighed = simulation.AddBody(new BodySettings { Shape = Crate });
        var floating = simulation.AddBody(new BodySettings { Shape = Crate, Position = new WorldPosition(5, 0, 0) });
        var damped = simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(10, 0, 0), GravityScale = 0,
            LinearVelocity = new Vector3F(1, 0, 0)
        });
        var wakeful = simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(15, 0, 0), GravityScale = 0
        });
        simulation.Step();

        simulation.SetMass(weighed, 3, 0);
        simulation.SetGravityScale(floating, 0);
        simulation.SetDamping(damped, 10, 0);
        simulation.SetCanSleep(wakeful, false);
        for (var step = 0; step < 120; step++)
        {
            simulation.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(simulation.MassOf(weighed), Is.EqualTo(3).Within(1e-4f));
            Assert.That(simulation.LinearVelocityOf(floating).Length(), Is.LessThan(0.2f), "barely pulled");
            Assert.That(simulation.LinearVelocityOf(damped).X, Is.LessThan(0.01f), "slowed to a stop");
            Assert.That(simulation.IsSleeping(wakeful), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetMass(weighed, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetDamping(weighed, float.NaN, 0));
        });
    }

    // While it runs, the caller's side asks and reads without allocating.
    [Test]
    public void WhileItRuns_UpdateAndActions_DoNotAllocate()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { TimeToSleep = float.MaxValue });
        simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(20, 1, 20), Position = new WorldPosition(0, -1, 0)
        });
        var crates = new BodyId[10];
        for (var index = 0; index < crates.Length; index++)
        {
            crates[index] = simulation.AddBody(new BodySettings
            {
                Shape = Crate, Position = new WorldPosition(index * 1.1 - 5, 0.5, 0)
            });
        }

        simulation.Start();
        void Frame()
        {
            simulation.ApplyImpulse(crates[0], new Vector3F(0, 0.01f, 0));
            simulation.SetForce(crates[1], new Vector3F(0, 1, 0));
            simulation.Update();
            simulation.InterpolatedPoseOf(crates[2]);
            simulation.RayCast(new WorldPosition(0, 10, 0), -Vector3F.UnitY, 20, out _);
        }

        // A frame once every run the thread may leave unread has piled up: as many results as ever go round, however
        // slow the machine - a busy one runs fewer, longer runs in a given time.
        void SlowFrame()
        {
            // Waited for without allocating, the last one being within what is measured. A thread that failed never
            // piles them up: the frame's Update throws what stopped it.
            var started = Stopwatch.GetTimestamp();
            var held = false;
            while (!held && Stopwatch.GetElapsedTime(started).TotalSeconds < 10)
            {
                held = simulation.IsHeldByUnreadResults;
                if (!held)
                    Thread.Sleep(5);
            }

            Frame();
            if (!held)
                Assert.Fail("The thread did not pile up the runs it may leave unread.");
        }

        // Frames as a game's: its buffers grow to what that takes while warming, and no more after.
        var warm = Stopwatch.StartNew();
        while (warm.ElapsedMilliseconds < 1000)
        {
            Frame();
            Thread.Sleep(1);
        }

        // Frames back to back, each asking more than a step's worth of frames does: the caller's list and the thread's
        // grow past what the frames below gather, however many of them fall into one step.
        for (var burst = 0; burst < 8; burst++)
        {
            for (var action = 0; action < 64; action++)
                simulation.ApplyImpulse(crates[0], new Vector3F(0, 0.0001f, 0));

            simulation.Update();
        }

        // Every batch goes round again, growing to the caller's list; what the burst left is run.
        var drain = Stopwatch.StartNew();
        while (drain.ElapsedMilliseconds < 200)
        {
            Frame();
            Thread.Sleep(1);
        }

        SlowFrame();
        SlowFrame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var beforeOnThread = simulation.ThreadAllocatedBytes;
        for (var frame = 0; frame < 200; frame++)
        {
            Frame();
            Thread.Sleep(1);
        }

        SlowFrame();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var allocatedOnThread = simulation.ThreadAllocatedBytes - beforeOnThread;
        simulation.Stop();

        Assert.That(allocated, Is.Zero, "the caller's thread");
        Assert.That(allocatedOnThread, Is.Zero, "the simulation's thread");
    }

    // Paused, the frames' actions outnumber the batches that go round; each is done once it goes on, in order.
    [Test]
    public void ActionsAskedWhilePaused_AreAllDone_OnceItGoesOn()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var body = simulation.AddBody(new BodySettings { Shape = Crate, Mass = 1, CanSleep = false });
        simulation.Start();
        UpdateUntil(simulation, () => simulation.StatusOf(body) == PhysicsStatus.Active);

        simulation.Pause();
        for (var frame = 0; frame < 50; frame++)
        {
            simulation.ApplyImpulse(body, new Vector3F(0.1f, 0, 0));
            simulation.Update();
        }

        simulation.SetLinearVelocity(body, new Vector3F(0, 0, 2));
        simulation.ApplyImpulse(body, new Vector3F(1, 0, 0));
        simulation.Update();
        var heldStill = simulation.LinearVelocityOf(body);
        simulation.Start();
        var done = UpdateUntil(simulation, () => simulation.LinearVelocityOf(body).Z > 1.9f);
        var velocity = simulation.LinearVelocityOf(body);
        simulation.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(heldStill, Is.EqualTo(Vector3F.Zero), "nothing done while paused");
            Assert.That(done, Is.True);
            Assert.That(velocity.X, Is.EqualTo(1).Within(1e-3f), "done in order: the velocity set over the impulses before");
        });
    }

    // Updates until the condition holds, up to a few seconds; whether it did.
    private static bool UpdateUntil(PhysicsSimulation simulation, Func<bool> condition)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(5))
        {
            simulation.Update();
            if (condition())
                return true;

            Thread.Sleep(2);
        }

        return false;
    }
}
