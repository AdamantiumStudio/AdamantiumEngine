using System;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>The simulation as a caller uses it: bodies and joints by id, actions done at the next step, what is read
/// what the last step left.</summary>
[TestFixture]
public class PhysicsSimulationTests
{
    private static readonly BoxCollisionShape Crate = new(0.5f, 0.5f, 0.5f);

    [Test]
    public void ABodyAdded_IsTakenByTheNextStep_AndFalls()
    {
        using var simulation = new PhysicsSimulation();
        var body = simulation.AddBody(new BodySettings { Shape = Crate, Position = new WorldPosition(0, 10, 0) });

        var before = simulation.StatusOf(body);
        var placed = simulation.PoseOf(body).Position;
        Run(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(PhysicsStatus.Adding));
            Assert.That(placed, Is.EqualTo(new WorldPosition(0, 10, 0)), "read as it was given until taken");
            Assert.That(simulation.StatusOf(body), Is.EqualTo(PhysicsStatus.Active));
            Assert.That(simulation.PoseOf(body).Position.Y, Is.EqualTo(10 - 0.5 * 9.81).Within(0.05));
            Assert.That(simulation.MassOf(body), Is.EqualTo(1000).Within(1e-3f), "a cubic meter of water");
            Assert.That(simulation.Bodies.ToArray(), Is.EqualTo(new[] { body }));
        });
    }

    [Test]
    public void ABodyTheCoreWillNotTake_IsRefused_WithWhy()
    {
        using var simulation = new PhysicsSimulation();
        var floor = new TriangleMeshCollisionShape([new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)], [0, 1, 2]);
        var body = simulation.AddBody(new BodySettings { Shape = floor });

        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.StatusOf(body), Is.EqualTo(PhysicsStatus.Refused));
            Assert.That(simulation.RefusalOf(body), Does.Contain("mesh"));
            Assert.That(simulation.Contains(body), Is.False);
            Assert.Throws<InvalidOperationException>(() => simulation.WakeUp(body));
        });
        simulation.RemoveBody(body);
        Assert.That(simulation.Bodies.IsEmpty, Is.True, "a refused body is taken out by hand");
        Assert.Throws<InvalidOperationException>(() => simulation.RemoveBody(body), "but once");
    }

    [Test]
    public void ARemovedBodysId_NamesNothing_AndItsSlotsNextBodyIsAnother()
    {
        using var simulation = new PhysicsSimulation();
        var first = simulation.AddBody(new BodySettings { Shape = Crate });
        simulation.Step();
        simulation.RemoveBody(first);
        simulation.Step();
        var second = simulation.AddBody(new BodySettings { Shape = Crate });

        Assert.Multiple(() =>
        {
            Assert.That(simulation.StatusOf(first), Is.EqualTo(PhysicsStatus.Removed));
            Assert.That(simulation.Contains(first), Is.False);
            Assert.Throws<InvalidOperationException>(() => simulation.ApplyImpulse(first, Vector3F.UnitX));
            Assert.That(second, Is.Not.EqualTo(first));
            Assert.That(simulation.Contains(second), Is.True);
        });
    }

    // An action is done at the next step: until then the body reads as the last step left it.
    [Test]
    public void ASetVelocity_IsReadAfterTheNextStep()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var body = simulation.AddBody(new BodySettings { Shape = Crate });
        simulation.Step();

        simulation.SetLinearVelocity(body, new Vector3F(2, 0, 0));
        var asked = simulation.LinearVelocityOf(body);
        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(Vector3F.Zero));
            Assert.That(simulation.LinearVelocityOf(body).X, Is.EqualTo(2).Within(1e-4f));
        });
    }

    // Made dynamic, a static body falls, and reads as it falls; its force set with it pushes from the same step.
    [Test]
    public void AStaticBodyMadeDynamic_FallsAndIsRead()
    {
        using var simulation = new PhysicsSimulation();
        var crate = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = Crate, Position = new WorldPosition(0, 10, 0)
        });
        simulation.Step();

        simulation.SetKind(crate, BodyKind.Static);
        simulation.SetKind(crate, BodyKind.Dynamic);
        simulation.SetForce(crate, new Vector3F(1000, 0, 0));
        Run(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.KindOf(crate), Is.EqualTo(BodyKind.Dynamic));
            Assert.That(simulation.PoseOf(crate).Position.Y, Is.LessThan(6), "it fell");
            Assert.That(simulation.LinearVelocityOf(crate).X, Is.EqualTo(1).Within(0.02f), "pushed");
        });
    }

    // Of a joint's changes refused at one step, the first is told.
    [Test]
    public void AJointsChangesRefused_TellTheFirst()
    {
        using var simulation = new PhysicsSimulation();
        var crate = simulation.AddBody(new BodySettings { Shape = Crate });
        var ball = simulation.AddJoint(new JointSettings { Kind = JointKind.Ball, First = crate });
        simulation.Step();

        simulation.SetLimits(ball, new JointLimits(-1, 1));
        simulation.SetMotor(ball, new JointMotor(1, 1));
        simulation.Step();

        Assert.That(simulation.RefusalOf(ball), Does.Contain("limits"));
    }

    // A kinematic body put elsewhere, standing still and so never awake, is read there, and so is its joint.
    [Test]
    public void AKinematicBodyPut_IsReadThere_WithItsJoint()
    {
        using var simulation = new PhysicsSimulation();
        var carriage = simulation.AddBody(new BodySettings { Kind = BodyKind.Kinematic, Shape = Crate });
        var rail = simulation.AddJoint(new JointSettings
        {
            Kind = JointKind.Slider, First = carriage, Axis = Vector3F.UnitX
        });
        Run(simulation, 1);

        simulation.SetPose(carriage, new Pose(new WorldPosition(0.5, 0, 0)));
        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.PoseOf(carriage).Position.X, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(simulation.PositionOf(rail), Is.EqualTo(0.5f).Within(1e-4f));
        });
    }

    // Sleeping at once, a body woken by a strike and asleep again within the step is read where the strike took it.
    [Test]
    public void ABodyWokenAndAsleepWithinAStep_IsReadWhereItWent()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings
        {
            Gravity = Vector3F.Zero, TimeToSleep = 0
        });
        var crate = simulation.AddBody(new BodySettings { Shape = Crate });
        Run(simulation, 0.1f);
        var asleep = simulation.IsSleeping(crate);

        simulation.ApplyImpulse(crate, new Vector3F(5, 0, 0));
        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(asleep, Is.True);
            Assert.That(simulation.PoseOf(crate).Position.X, Is.Positive);
        });
    }

    // Asleep, and quick to sleep again, a crate struck by another - no command at it - is read as it is pushed.
    [Test]
    public void ASleeperStruckByAnother_IsReadAsItIsPushed()
    {
        var settings = new PhysicsSettings();
        using var simulation = new PhysicsSimulation(new PhysicsSettings
        {
            Gravity = Vector3F.Zero, TimeToSleep = 1.5f / settings.StepRate
        });
        var sleeper = simulation.AddBody(new BodySettings { Shape = Crate });
        Run(simulation, 0.1f);
        var asleep = simulation.IsSleeping(sleeper);

        simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(-3, 0, 0), LinearVelocity = new Vector3F(10, 0, 0)
        });
        Run(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(asleep, Is.True);
            Assert.That(simulation.PoseOf(sleeper).Position.X, Is.GreaterThan(0.5), "pushed on");
        });
    }

    // Asleep, a body reads as it lay; woken, as it moves.
    [Test]
    public void ABodyAsleep_ReadsAsItLay_AndWokenAsItMoves()
    {
        using var simulation = new PhysicsSimulation();
        simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(10, 1, 10), Position = new WorldPosition(0, -1, 0)
        });
        var crate = simulation.AddBody(new BodySettings { Shape = Crate, Position = new WorldPosition(0, 0.5, 0) });
        var steps = 0;
        while (!simulation.IsSleeping(crate) && steps++ < 600)
        {
            simulation.Step();
        }

        var lying = simulation.PoseOf(crate).Position;
        Run(simulation, 0.5f);
        var stillLying = simulation.PoseOf(crate).Position;
        simulation.ApplyImpulse(crate, new Vector3F(0, 5000, 0));
        Run(simulation, 0.1f);

        Assert.Multiple(() =>
        {
            Assert.That(steps, Is.LessThan(600), "it fell asleep");
            Assert.That(stillLying, Is.EqualTo(lying));
            Assert.That(simulation.IsSleeping(crate), Is.False);
            Assert.That(simulation.PoseOf(crate).Position.Y, Is.GreaterThan(lying.Y + 0.2), "thrown up");
            Assert.That(simulation.LinearVelocityOf(crate).Y, Is.Positive);
        });
    }

    // A force pushes at every step until set again; an impulse strikes once.
    [Test]
    public void AForce_PushesEveryStep_AndAnImpulseOnce()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var pushed = simulation.AddBody(new BodySettings { Shape = Crate, Mass = 2 });
        var struck = simulation.AddBody(new BodySettings
        {
            Shape = Crate, Mass = 2, Position = new WorldPosition(5, 0, 0)
        });
        simulation.Step();

        simulation.SetForce(pushed, new Vector3F(4, 0, 0));
        simulation.ApplyImpulse(struck, new Vector3F(4, 0, 0));
        Run(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.LinearVelocityOf(pushed).X, Is.EqualTo(2).Within(0.02f), "two meters per second a second");
            Assert.That(simulation.LinearVelocityOf(struck).X, Is.EqualTo(2).Within(1e-4f), "two at once, and kept");
        });
    }

    [Test]
    public void AnImpulseOffTheCenter_AndAnAngularOne_TurnTheBody()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var offCenter = simulation.AddBody(new BodySettings { Shape = Crate });
        var spun = simulation.AddBody(new BodySettings { Shape = Crate, Position = new WorldPosition(5, 0, 0) });
        simulation.Step();

        simulation.ApplyImpulse(offCenter, new Vector3F(0, 0, 10), new WorldPosition(0.5, 0, 0));
        simulation.ApplyAngularImpulse(spun, new Vector3F(0, 10, 0));
        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.AngularVelocityOf(offCenter).Y, Is.Not.Zero);
            Assert.That(simulation.AngularVelocityOf(spun).Y, Is.Positive);
            Assert.That(simulation.LinearVelocityOf(spun).Length(), Is.LessThan(1e-5f), "turned in place");
        });
    }

    // Moved to a pose over one step, a kinematic body stands there and stops, unless moved on.
    [Test]
    public void AKinematicBodyMoved_StandsWhereItWasSentAfterTheStep()
    {
        using var simulation = new PhysicsSimulation();
        var platform = simulation.AddBody(new BodySettings { Kind = BodyKind.Kinematic, Shape = Crate });
        simulation.Step();
        var turn = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, 0.2f);

        simulation.MoveKinematic(platform, new Pose(new WorldPosition(0.3, 0, -0.1), turn));
        simulation.Step();
        var arrived = simulation.PoseOf(platform);
        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That((arrived.Position - new WorldPosition(0.3, 0, -0.1)).Length(), Is.LessThan(1e-4));
            Assert.That(QuaternionF.Dot(arrived.Rotation, turn), Is.GreaterThan(0.99999f));
            Assert.That((simulation.PoseOf(platform).Position - arrived.Position).Length(), Is.LessThan(1e-6),
                "stopped there");
            Assert.Throws<InvalidOperationException>(() =>
                simulation.MoveKinematic(simulation.AddBody(new BodySettings { Shape = Crate }), Pose.Identity));
        });
    }

    // Moved in a frame of several steps, a kinematic body arrives in the first and rests asleep for the rest: it is
    // read there all the same.
    [Test]
    public void AKinematicBodyMovedInAFrameOfSeveralSteps_IsReadWhereItWasSent()
    {
        using var simulation = new PhysicsSimulation();
        var platform = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Kinematic, Shape = Crate, Position = new WorldPosition(9, -3, 0)
        });
        simulation.Advance(2 * simulation.StepSeconds);

        simulation.MoveKinematic(platform, new Pose(new WorldPosition(9, -4, 0)));
        var steps = simulation.Advance(2.5 * simulation.StepSeconds);

        Assert.That(steps, Is.GreaterThanOrEqualTo(2));
        Assert.That(simulation.PoseOf(platform).Position.Y, Is.EqualTo(-4).Within(1e-4));
    }

    // Moved and then given a velocity in the same frame, a kinematic body keeps that velocity after the step.
    [Test]
    public void AVelocitySetAfterAKinematicMove_IsKept()
    {
        using var simulation = new PhysicsSimulation();
        var platform = simulation.AddBody(new BodySettings { Kind = BodyKind.Kinematic, Shape = Crate });
        simulation.Step();

        simulation.MoveKinematic(platform, new Pose(new WorldPosition(0, 0, 1), Quaternion.CreateFromAxisAngle(Vector3F.UnitY, 0.1f)));
        simulation.SetLinearVelocity(platform, new Vector3F(1, 0, 0));
        simulation.Step();
        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(simulation.LinearVelocityOf(platform).X, Is.EqualTo(1).Within(1e-5f));
            Assert.That(simulation.AngularVelocityOf(platform).Length(), Is.LessThan(1e-6f), "the turn stopped");
        });
    }

    // A change the core will not do leaves the body as it was, and what it is checked by with it.
    [Test]
    public void AChangeRefused_LeavesTheBodyAsItWas_ForAStepsTelling()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var floor = new TriangleMeshCollisionShape([new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)], [0, 1, 2]);
        var plate = simulation.AddBody(new BodySettings { Kind = BodyKind.Kinematic, Shape = floor });
        ReadOnlySpan<BodyPart> parts =
        [
            new BodyPart { Shape = Crate, Data = "near" },
            new BodyPart { Shape = Crate, Pose = new LocalPose(new Vector3F(2, 0, 0)), Data = "far" }
        ];
        var pair = simulation.AddBody(new BodySettings { Parts = parts, Position = new WorldPosition(10, 0, 0) });
        simulation.Step();

        simulation.SetKind(plate, BodyKind.Dynamic);
        simulation.SetForce(plate, Vector3F.UnitY);
        simulation.SetShape(pair, floor);
        simulation.Step();
        var plateRefusal = simulation.RefusalOf(plate);
        var pairRefusal = simulation.RefusalOf(pair);
        simulation.MoveKinematic(plate, new Pose(new WorldPosition(0, 1, 0)));
        simulation.Step();

        Assert.Multiple(() =>
        {
            Assert.That(plateRefusal, Does.Contain("volume"), "the cause told, not the push that followed from it");
            Assert.That(pairRefusal, Is.Not.Null);
            Assert.That(simulation.KindOf(plate), Is.EqualTo(BodyKind.Kinematic));
            Assert.That(simulation.PoseOf(plate).Position.Y, Is.EqualTo(1).Within(1e-4), "moved as kinematic still");
            Assert.That(simulation.DataOf(pair, 1), Is.EqualTo("far"), "its parts kept");
            Assert.That(simulation.RefusalOf(plate), Is.Null, "told for one step");
        });
    }

    // A torque too hard for the body is dropped; its force pushes on.
    [Test]
    public void ATorqueTooHard_IsDropped_AndTheForcePushesOn()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var speck = simulation.AddBody(new BodySettings { Shape = new SphereCollisionShape(0.01f), Mass = 1e-6f });
        simulation.Step();

        simulation.SetForce(speck, new Vector3F(1e-6f, 0, 0));
        simulation.SetTorque(speck, new Vector3F(float.MaxValue, 0, 0));
        Run(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.LinearVelocityOf(speck).X, Is.EqualTo(1).Within(0.02f));
            Assert.That(simulation.AngularVelocityOf(speck).Length(), Is.Zero);
        });
    }

    [Test]
    public void APendulumHeldToTheWorld_SwingsOnItsRod_AndGoesWithItsBody()
    {
        using var simulation = new PhysicsSimulation();
        var ball = simulation.AddBody(new BodySettings
        {
            Shape = new SphereCollisionShape(0.1f), Position = new WorldPosition(2, 5, 0)
        });
        var rod = simulation.AddJoint(new JointSettings
        {
            Kind = JointKind.Distance, First = ball, Point = new WorldPosition(2, 5, 0),
            SecondPoint = new WorldPosition(0, 5, 0)
        });

        Run(simulation, 1);
        var length = simulation.PositionOf(rod);
        simulation.RemoveBody(ball);

        Assert.Multiple(() =>
        {
            Assert.That(length, Is.EqualTo(2).Within(0.02f));
            Assert.That(simulation.PoseOf(ball).Position.Y, Is.LessThan(5), "it swung down");
            Assert.That(simulation.StatusOf(rod), Is.EqualTo(PhysicsStatus.Removed), "the rod went with the ball");
        });
    }

    // A hinge limited to a quarter turn and driven past it stops at its limit; freed, its motor turns it on.
    [Test]
    public void AHingeDrivenToItsLimit_StopsThere_AndFreedTurnsOn()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        var wheel = simulation.AddBody(new BodySettings { Shape = new SphereCollisionShape(0.5f) });
        var hinge = simulation.AddJoint(new JointSettings
        {
            Kind = JointKind.Hinge, First = wheel, Axis = Vector3F.UnitX, Motor = new JointMotor(2, 5000),
            Limits = new JointLimits(-MathF.PI / 2, MathF.PI / 2)
        });

        Run(simulation, 2);
        var held = simulation.PositionOf(hinge);
        simulation.SetLimits(hinge, null);
        Run(simulation, 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(held), Is.EqualTo(MathF.PI / 2).Within(0.05f), "at its limit");
            Assert.That(Math.Abs(simulation.PositionOf(hinge)), Is.GreaterThan(MathF.PI / 2 + 0.3f), "turning on");
        });
    }

    [Test]
    public void AJointOfARefusedBody_IsRefusedToo()
    {
        using var simulation = new PhysicsSimulation();
        var floor = new TriangleMeshCollisionShape([new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)], [0, 1, 2]);
        var refused = simulation.AddBody(new BodySettings { Shape = floor });
        var joint = simulation.AddJoint(new JointSettings { First = refused });

        simulation.Step();

        Assert.That(simulation.StatusOf(joint), Is.EqualTo(PhysicsStatus.Refused));
    }

    // Queries tell the body and its part met, the caller's own of each given back, and pass by the body asked.
    [Test]
    public void Queries_TellTheBodyAndPartMet_AndPassByTheOneAsked()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { Gravity = Vector3F.Zero });
        ReadOnlySpan<BodyPart> parts =
        [
            new BodyPart { Shape = Crate, Pose = new LocalPose(new Vector3F(-1, 0, 0)), Data = "left" },
            new BodyPart { Shape = Crate, Pose = new LocalPose(new Vector3F(1, 0, 0)), Data = "right" }
        ];
        var pair = simulation.AddBody(new BodySettings { Kind = BodyKind.Static, Parts = parts, Data = "pair" });
        var far = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = Crate, Position = new WorldPosition(1, -5, 0)
        });
        simulation.Step();

        var met = simulation.RayCast(new WorldPosition(1, 10, 0), -Vector3F.UnitY, 30, out var hit);
        Span<RayHit> hits = stackalloc RayHit[4];
        var all = simulation.RayCast(new WorldPosition(1, 10, 0), -Vector3F.UnitY, 30, hits);
        var passed = simulation.RayCast(new WorldPosition(1, 10, 0), -Vector3F.UnitY, 30, out var beyond,
            new QueryFilter { Except = pair });
        Span<BodyId> found = stackalloc BodyId[4];
        var overlapping = simulation.Overlap(new SphereCollisionShape(3), Pose.Identity, found);
        var cast = simulation.CastShape(new SphereCollisionShape(0.2f), new Pose(new WorldPosition(1, 10, 0)),
            new Vector3F(0, -30, 0), out var castHit, new QueryFilter { Except = pair });
        var nearest = hits[0].Body;
        var further = hits[1].Body;
        var overlapped = found[0];

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Body, Is.EqualTo(pair));
            Assert.That(hit.Part, Is.EqualTo(1));
            Assert.That(simulation.DataOf(hit.Body, hit.Part), Is.EqualTo("right"));
            Assert.That(simulation.DataOf(pair), Is.EqualTo("pair"));
            Assert.That(all, Is.EqualTo(2));
            Assert.That(nearest, Is.EqualTo(pair), "nearest first");
            Assert.That(further, Is.EqualTo(far));
            Assert.That(passed, Is.True);
            Assert.That(beyond.Body, Is.EqualTo(far), "the pair passed by");
            Assert.That(overlapping, Is.EqualTo(1));
            Assert.That(overlapped, Is.EqualTo(pair));
            Assert.That(cast, Is.True);
            Assert.That(castHit.Body, Is.EqualTo(far));
        });
    }

    [Test]
    public void BadAsks_AreRefusedAtOnce()
    {
        using var simulation = new PhysicsSimulation();
        var ground = simulation.AddBody(new BodySettings { Kind = BodyKind.Static, Shape = Crate });
        var crate = simulation.AddBody(new BodySettings { Shape = Crate });

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => simulation.AddBody(new BodySettings()), "no shape");
            Assert.Throws<ArgumentException>(() => simulation.AddBody(new BodySettings
            {
                Shape = Crate, Parts = [new BodyPart { Shape = Crate }]
            }), "a shape and parts");
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.AddBody(new BodySettings
            {
                Shape = Crate, Position = new WorldPosition(double.NaN, 0, 0)
            }));
            Assert.Throws<InvalidOperationException>(() => simulation.ApplyImpulse(ground, Vector3F.UnitX));
            Assert.Throws<InvalidOperationException>(() => simulation.SetLinearVelocity(ground, Vector3F.UnitX));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                simulation.SetForce(crate, new Vector3F(float.PositiveInfinity, 0, 0)));
            Assert.Throws<ArgumentException>(() => simulation.AddJoint(new JointSettings { First = crate, Second = crate }));
            Assert.Throws<InvalidOperationException>(() => simulation.PoseOf(default));
        });
    }

    // A warmed simulation steps, takes its actions and answers its queries without allocating.
    [Test]
    public void AWarmedSimulation_StepsActsAndQueries_WithoutAllocating()
    {
        using var simulation = new PhysicsSimulation(new PhysicsSettings { TimeToSleep = float.MaxValue });
        var ground = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(20, 1, 20), Position = new WorldPosition(0, -1, 0)
        });
        var floor = new TriangleMeshCollisionShape([new(0, 0, 0), new(1, 0, 0), new(0, 0, 1)], [0, 1, 2]);
        var plate = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Kinematic, Shape = floor, Position = new WorldPosition(30, 0, 0)
        });
        var crates = new BodyId[20];
        for (var index = 0; index < crates.Length; index++)
        {
            crates[index] = simulation.AddBody(new BodySettings
            {
                Shape = Crate, Position = new WorldPosition(index % 5 * 1.1 - 2, 0.5 + index / 5, 0)
            });
        }

        var frameNumber = 0;
        void Frame()
        {
            frameNumber++;
            simulation.ApplyImpulse(crates[0], new Vector3F(0, 0.01f, 0));
            simulation.SetLinearVelocity(crates[1], Vector3F.Zero);
            simulation.SetForce(crates[2], new Vector3F(0, frameNumber % 2, 0));
            simulation.SetPose(ground, new Pose(new WorldPosition(0, -1, 0)));
            simulation.MoveKinematic(plate, new Pose(new WorldPosition(30, frameNumber % 3, 0)));
            simulation.SetKind(crates[3], frameNumber % 2 == 0 ? BodyKind.Kinematic : BodyKind.Dynamic);
            simulation.Step();
            simulation.RayCast(new WorldPosition(0, 10, 0), -Vector3F.UnitY, 20, out _);
            Span<BodyId> near = stackalloc BodyId[8];
            simulation.Overlap(Crate, Pose.Identity, near);
        }

        for (var frame = 0; frame < 300; frame++)
        {
            Frame();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 120; frame++)
        {
            Frame();
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
    }

    private static void Run(PhysicsSimulation simulation, float seconds)
    {
        for (var step = 0; step < seconds * simulation.Settings.StepRate; step++)
        {
            simulation.Step();
        }
    }
}
