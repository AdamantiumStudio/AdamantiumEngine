using System;
using System.Diagnostics;
using System.Threading;
using Adamantium.Core;
using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Physics;
using Adamantium.Engine.Scenes;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The physics of a game: the scene's rigid bodies fall, rest and take pushes while it plays, and the
/// entities follow them. Down is +Y in the engine's world.</summary>
[TestFixture]
public class PhysicsServiceTests
{
    private const double FrameSeconds = 1.0 / 60;

    private TestUniverse _universe;
    private EntityWorld _world;
    private IPhysicsService _physics;
    private GameRun _run;

    [SetUp]
    public void MakeWorld()
    {
        _universe = new TestUniverse();
        _world = _universe.EntityWorld;
    }

    [TearDown]
    public void StopGame()
    {
        _run?.Stop();
    }

    [Test]
    public void BoxDropped_LandsOnTheGroundAndRests()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -3, 0), 0.5);

        Play();
        Frames(3);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
        Assert.That(Math.Abs(box.Transform.Position.X) + Math.Abs(box.Transform.Position.Z), Is.LessThan(0.01));
        Assert.That(_physics.GetLinearVelocity(box).Length(), Is.LessThan(0.05f));
    }

    [Test]
    public void BodyUnderAParent_FallsInTheWorldAndKeepsItsParent()
    {
        UseSteppedPhysics();
        AddGround();
        var parent = new Entity(null, "Parent");
        parent.Transform.Position = new Vector3<double>(5, 0, 0);
        _world.EntityManager.AddEntity(parent, _world.MainScene);
        var box = MakeBox("Child", new Vector3<double>(0, -3, 0), 0.5, parent);
        Settle();

        Play();
        Frames(3);

        Assert.That(box.Owner, Is.SameAs(parent));
        Assert.That(box.Transform.WorldPosition.X, Is.EqualTo(5).Within(0.02));
        Assert.That(box.Transform.WorldPosition.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    [Test]
    public void Impulse_PushesTheBodyAlong()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -0.5, 0), 0.5);
        Play();
        Frames(0.5);

        _physics.ApplyImpulse(box, new Vector3F(5000, 0, 0));
        Frames(2);

        var expected = 5.0 * 5.0 / (2 * 0.6 * 9.81);
        Assert.That(box.Transform.Position.X, Is.EqualTo(expected).Within(0.1 * expected));
    }

    [Test]
    public void TurnedBox_KeepsItsTurnWhileItRests()
    {
        UseSteppedPhysics();
        AddGround();
        var box = MakeBox("Turned", new Vector3<double>(0, -0.5, 0), 0.5);
        var turn = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, 0.6f);
        box.Transform.Rotation = turn;
        _world.EntityManager.AddEntity(box, _world.MainScene);
        Settle();

        Play();
        Frames(1);

        Assert.That(MathF.Abs(QuaternionF.Dot(box.Transform.Rotation, turn)), Is.GreaterThan(0.9999f));
    }

    [Test]
    public void BodiesWithoutAShapeTheyCanUse_AreLeftOut()
    {
        UseSteppedPhysics();
        var bare = new Entity(null, "Bare");
        bare.AddComponent(new RigidBody());
        _world.EntityManager.AddEntity(bare, _world.MainScene);
        var hull = new Entity(null, "Hull");
        hull.AddComponent(new ConvexHullCollider());
        hull.AddComponent(new RigidBody());
        _world.EntityManager.AddEntity(hull, _world.MainScene);
        Settle();

        Play();
        Frames(0.5);

        Assert.That(_physics.Moves(bare), Is.False);
        Assert.That(_physics.Moves(hull), Is.False);
        Assert.That(bare.Transform.Position, Is.EqualTo(Vector3<double>.Zero));
    }

    [Test]
    public void StoppedGame_LetsTheBodiesGo()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -3, 0), 0.5);
        Play();
        Frames(0.2);

        _run.Stop();
        _run = null;

        Assert.That(_physics.Moves(box), Is.False);
        Assert.Throws<ArgumentException>(() => _physics.ApplyImpulse(box, Vector3F.UnitX));
    }

    [Test]
    public void SceneKeptBeforeTheGame_PutsTheBodiesBackAfterIt()
    {
        UseSteppedPhysics();
        AddGround();
        var start = new Vector3<double>(0, -3, 0);
        var box = AddBox("Box", start, 0.5);
        var snapshot = SceneSnapshot.Take(_world.MainScene);
        Play();
        Frames(2);
        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));

        _run.Stop();
        _run = null;
        snapshot.Restore();

        Assert.That(box.Transform.Position, Is.EqualTo(start));
    }

    [Test]
    public void PausedGame_HoldsTheBodiesStill()
    {
        _physics = new PhysicsService(_world);
        _world.AddService(_physics);
        Settle();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -3, 0), 0.5);
        Play();
        _run.IsPaused = true;

        Thread.Sleep(300);
        Frame(0.3);
        var held = box.Transform.Position;
        _run.IsPaused = false;
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5) && Math.Abs(box.Transform.Position.Y + 0.5) > 0.02)
        {
            Thread.Sleep(10);
            Frame(0.01);
        }

        Assert.That(held.Y, Is.EqualTo(-3).Within(1e-6));
        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    [Test]
    public void SteadyForce_PushesAsMuchAtAnyFrameRate()
    {
        var slow = MeasureSpeedAfterSteadyForce(1.0 / 30);
        var fast = MeasureSpeedAfterSteadyForce(1.0 / 144);

        Assert.That(slow, Is.EqualTo(1f).Within(0.05f));
        Assert.That(fast, Is.EqualTo(1f).Within(0.05f));
    }

    [Test]
    public void ForceHoldingABodyUp_HoldsItThroughSlowFrames()
    {
        UseSteppedPhysics();
        var box = AddBox("Held", new Vector3<double>(0, -3, 0), 0.5);
        Play();
        var weight = new Vector3F(0, -1000 * 9.81f, 0);
        for (var frame = 0; frame < 8; frame++)
        {
            _physics.ApplyForce(box, weight);
            Frame(0.5);
        }

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-3).Within(0.05));
    }

    [Test]
    public void PushOfASlowFrame_EndsWithTheTimeSimulated()
    {
        UseSteppedPhysics();
        _physics.Gravity = Vector3F.Zero;
        var box = AddBox("Pushed", Vector3<double>.Zero, 0.5);
        Play();

        _physics.ApplyForce(box, new Vector3F(1000, 0, 0));
        Frame(0.5);
        Frames(1);

        var simulated = 8.0 / 120;
        Assert.That(_physics.GetLinearVelocity(box).X, Is.EqualTo(simulated).Within(0.01));
    }

    [Test]
    public void BodyTurnedUnderAStretchedParent_IsLeftOut()
    {
        UseSteppedPhysics();
        var parent = new Entity(null, "Stretched");
        parent.Transform.ScaleFactor = new Vector3F(4, 1, 1);
        _world.EntityManager.AddEntity(parent, _world.MainScene);
        var box = MakeBox("Skewed", Vector3<double>.Zero, 0.5, parent);
        box.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 4);
        Settle();

        Play();
        Frames(0.2);

        Assert.That(_physics.Moves(box), Is.False);
    }

    [Test]
    public void MirroredEntity_IsLeftOut()
    {
        UseSteppedPhysics();
        var mirrored = MakeBox("Mirrored", new Vector3<double>(0, -3, 0), 0.5);
        mirrored.Transform.BaseScale = new Vector3F(1, -1, 1);
        _world.EntityManager.AddEntity(mirrored, _world.MainScene);
        Settle();

        Play();
        Frames(0.5);

        Assert.That(_physics.Moves(mirrored), Is.False);
        Assert.That(mirrored.Transform.Position.Y, Is.EqualTo(-3).Within(1e-9));
    }

    [Test]
    public void NewSettings_KeepGravityPointingDown()
    {
        UseSteppedPhysics();
        _physics.Settings = new PhysicsSettings { StepRate = 60 };
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -3, 0), 0.5);

        Play();
        Frames(3);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    [Test]
    public void SleepingChild_StaysWhereItRestsWhenItsParentMoves()
    {
        UseSteppedPhysics();
        AddGround();
        var parent = new Entity(null, "Parent");
        _world.EntityManager.AddEntity(parent, _world.MainScene);
        var box = MakeBox("Child", new Vector3<double>(0, -0.5, 0), 0.5, parent);
        Settle();
        Play();
        Frames(2);

        parent.Transform.Position = new Vector3<double>(3, 0, 0);
        Frames(0.1);

        Assert.That(box.Transform.WorldPosition.X, Is.EqualTo(0).Within(0.02));
        Assert.That(box.Transform.Position.X, Is.EqualTo(-3).Within(0.02));
    }

    [Test]
    public void OwnThread_StepsTheGameInRealTime()
    {
        _physics = new PhysicsService(_world);
        _world.AddService(_physics);
        Settle();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -3, 0), 0.5);

        Play();
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5) && Math.Abs(box.Transform.Position.Y + 0.5) > 0.02)
        {
            Thread.Sleep(10);
            Frame(0.01);
        }

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
        Assert.That(_physics.LastStep.TouchingPairs, Is.EqualTo(1));
    }

    private static float MeasureSpeedAfterSteadyForce(double frameSeconds)
    {
        var tests = new PhysicsServiceTests();
        tests.MakeWorld();
        tests.UseSteppedPhysics();
        tests._physics.Gravity = Vector3F.Zero;
        var box = tests.AddBox("Pushed", Vector3<double>.Zero, 0.5);
        tests.Play();
        var frames = (int)Math.Round(1 / frameSeconds);
        for (var frame = 0; frame < frames; frame++)
        {
            tests.Frame(frameSeconds);
            tests._physics.ApplyForce(box, new Vector3F(1000, 0, 0));
        }

        tests.Frames(0.1);
        var speed = tests._physics.GetLinearVelocity(box).X;
        tests.StopGame();
        return speed;
    }

    private void UseSteppedPhysics()
    {
        _physics = new PhysicsService(_world, stepsOnOwnThread: false);
        _world.AddService(_physics);
        Settle();
    }

    private void AddGround()
    {
        var ground = new Entity(null, "Ground");
        ground.Transform.Position = new Vector3<double>(0, 1, 0);
        ground.AddComponent(new BoxCollider
        {
            Bounds = new Bounds(Vector3<double>.Zero, new Vector3<double>(20, 1, 20), Quaternion<double>.Identity)
        });
        ground.AddComponent(new RigidBody { Kind = BodyKind.Static });
        _world.EntityManager.AddEntity(ground, _world.MainScene);
        Settle();
    }

    private Entity AddBox(string name, Vector3<double> position, double half)
    {
        var box = MakeBox(name, position, half);
        _world.EntityManager.AddEntity(box, _world.MainScene);
        Settle();
        return box;
    }

    private static Entity MakeBox(string name, Vector3<double> position, double half, Entity owner = null)
    {
        var box = new Entity(owner, name);
        box.Transform.Position = position;
        box.AddComponent(new BoxCollider
        {
            Bounds = new Bounds(Vector3<double>.Zero, new Vector3<double>(half), Quaternion<double>.Identity)
        });
        box.AddComponent(new RigidBody());
        return box;
    }

    private void Play()
    {
        _run = GameRun.Start(_world.MainScene);
    }

    private void Frames(double seconds)
    {
        var frames = (int)Math.Round(seconds / FrameSeconds);
        for (var frame = 0; frame < frames; frame++)
        {
            Frame(FrameSeconds);
        }
    }

    private void Frame(double seconds)
    {
        _world.ServiceManager.Update(new AppTime { FrameTime = seconds });
    }

    private void Settle()
    {
        _world.ForceUpdate();
        _universe.Settle();
    }
}
