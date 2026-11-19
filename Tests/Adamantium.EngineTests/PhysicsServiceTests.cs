using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Adamantium.Core;
using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Physics;
using Adamantium.Engine.Scenes;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
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
        Assert.That(_physics.Moves(hull), Is.False, "a hull collider with no mesh holds no hull");
        Assert.That(bare.Transform.Position, Is.EqualTo(Vector3<double>.Zero));
    }

    [Test]
    public void HullOfAMesh_FallsAndRestsOnItsFace()
    {
        UseSteppedPhysics();
        AddGround();
        // A wedge: a square face down at its origin, a ridge up; scaled twice over.
        var wedge = AddHull("Wedge", new Mesh(PrimitiveType.TriangleList).SetPoints(
        [
            new Vector3<double>(-0.5, 0, -0.5), new Vector3<double>(0.5, 0, -0.5), new Vector3<double>(0.5, 0, 0.5),
            new Vector3<double>(-0.5, 0, 0.5), new Vector3<double>(-0.5, -0.5, 0), new Vector3<double>(0.5, -0.5, 0)
        ]), new Vector3<double>(0, -2, 0), 2);
        Settle();

        Play();
        Frames(3);

        Assert.That(_physics.Moves(wedge), Is.True);
        Assert.That(wedge.Transform.Position.Y, Is.EqualTo(0).Within(0.02), "its square face, at its origin, on the ground");
        Assert.That(Vector3<float>.Transform(Vector3<float>.UnitY, wedge.Transform.Rotation.As<float>()).Y,
            Is.GreaterThan(0.999f));
    }

    // Three cubes of one mesh, two of them twice the first: each rests by its own size, though a hull is made once for
    // each scale.
    [Test]
    public void HullsOfOneMesh_RestEachByItsOwnSize()
    {
        UseSteppedPhysics();
        AddGround();
        var corners = new List<Vector3<double>>();
        foreach (var x in new[] { -0.5, 0.5 })
        foreach (var y in new[] { -0.5, 0.5 })
        foreach (var z in new[] { -0.5, 0.5 })
        {
            corners.Add(new Vector3<double>(x, y, z));
        }

        var mesh = new Mesh(PrimitiveType.TriangleList).SetPoints(corners.ToArray());
        var small = AddHull("Small", mesh, new Vector3<double>(-3, -2, 0), 1);
        var large = AddHull("Large", mesh, new Vector3<double>(3, -3, 0), 2);
        var twin = AddHull("Twin", mesh, new Vector3<double>(0, -3, 3), 2);
        Settle();

        Play();
        Frames(3);

        Assert.That(small.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
        Assert.That(large.Transform.Position.Y, Is.EqualTo(-1).Within(0.02));
        Assert.That(twin.Transform.Position.Y, Is.EqualTo(-1).Within(0.02));
    }

    // A log: a cylinder collider fitted along its long side, dropped turned - it rests on its round side, its axis
    // level, its middle a radius above the ground.
    [Test]
    public void LogOfACylinderCollider_FallsAndRestsOnItsRoundSide()
    {
        UseSteppedPhysics();
        AddGround();
        var corners = new List<Vector3<double>>();
        foreach (var x in new[] { -0.4, 0.4 })
        foreach (var y in new[] { -0.4, 0.4 })
        foreach (var z in new[] { -1.0, 1.0 })
        {
            corners.Add(new Vector3<double>(x, y, z));
        }

        var log = new Entity(null, "Log");
        log.Transform.Position = new Vector3<double>(0, -2, 0);
        log.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, 0.4f);
        log.AddComponent(new MeshData { Mesh = new Mesh(PrimitiveType.TriangleList).SetPoints(corners.ToArray()) });
        log.AddComponent(new CylinderCollider());
        log.AddComponent(new RigidBody());
        _world.EntityManager.AddEntity(log, _world.MainScene);
        Settle();

        Play();
        Frames(3);

        Assert.That(_physics.Moves(log), Is.True);
        Assert.That(log.Transform.Position.Y, Is.EqualTo(-0.4).Within(0.02), "a radius above the ground");
        var axis = Vector3<float>.Transform(Vector3<float>.UnitZ, log.Transform.Rotation);
        Assert.That(MathF.Abs(axis.Y), Is.LessThan(0.01f), "its axis level");
    }

    private Entity AddHull(string name, Mesh mesh, Vector3<double> position, float scale)
    {
        var entity = new Entity(null, name);
        entity.Transform.Position = position;
        entity.Transform.BaseScale = new Vector3<float>(scale);
        entity.AddComponent(new MeshData { Mesh = mesh });
        entity.AddComponent(new ConvexHullCollider());
        entity.AddComponent(new RigidBody());
        _world.EntityManager.AddEntity(entity, _world.MainScene);
        return entity;
    }

    [Test]
    public void TableOfChildEntities_StandsLevelOnItsLegs()
    {
        UseSteppedPhysics();
        AddGround();
        var table = new Entity(null, "Table");
        table.Transform.Position = new Vector3<double>(0, -0.2, 0);
        table.AddComponent(new RigidBody());
        AddPart(table, "Top", new Vector3<double>(0, -0.85, 0), new Vector3<double>(1, 0.05, 0.6));
        foreach (var corner in new[] { new Vector3<double>(0.9, -0.4, 0.5), new Vector3<double>(-0.9, -0.4, 0.5),
                     new Vector3<double>(0.9, -0.4, -0.5), new Vector3<double>(-0.9, -0.4, -0.5) })
        {
            AddPart(table, "Leg", corner, new Vector3<double>(0.05, 0.4, 0.05));
        }

        _world.EntityManager.AddEntity(table, _world.MainScene);
        Settle();

        Play();
        Frames(2);

        Assert.That(table.Transform.Position.Y, Is.EqualTo(0).Within(0.02), "it stands on the feet of its legs");
        Assert.That(Vector3<float>.Transform(Vector3<float>.UnitY, table.Transform.Rotation.As<float>()).Y,
            Is.GreaterThan(0.9999f));
        Assert.That(table.Dependencies[0].Transform.Position.Y, Is.EqualTo(-0.85).Within(1e-9), "its parts stay put on it");
    }

    // Drawn between two steps, a body turns about its center of mass: its origin, off it, swings round, not across.
    [Test]
    public void SpinningBodyDrawnBetweenSteps_TurnsAboutItsCenterOfMass()
    {
        UseSteppedPhysics();
        _physics.Gravity = Vector3<float>.Zero;
        var spinner = new Entity(null, "Spinner");
        spinner.AddComponent(new RigidBody { CanSleep = false });
        AddPart(spinner, "Weight", new Vector3<double>(2, 0, 0), new Vector3<double>(0.5));
        _world.EntityManager.AddEntity(spinner, _world.MainScene);
        Settle();
        Play();
        _physics.SetAngularVelocity(spinner, new Vector3<float>(0, 20, 0));
        Frame(FrameSeconds);

        var farthest = 0.0;
        for (var frame = 0; frame < 24; frame++)
        {
            Frame(1.0 / 240);
            var weight = spinner.Transform.Position
                         + Vector3<float>.Transform(new Vector3<float>(2, 0, 0), spinner.Transform.Rotation).As<double>();
            farthest = Math.Max(farthest, (weight - new Vector3<double>(2, 0, 0)).Length());
        }

        Assert.That(spinner.Transform.Rotation, Is.Not.EqualTo(Quaternion<float>.Identity), "it spins");
        Assert.That(farthest, Is.LessThan(1e-3), "the weight, its center of mass, stays where it is");
    }

    [Test]
    public void ChildWithABodyOfItsOwn_IsNoPartOfItsParent()
    {
        UseSteppedPhysics();
        AddGround();
        var parent = new Entity(null, "Cart");
        parent.AddComponent(new RigidBody());
        var child = MakeBox("Crate", new Vector3<double>(0, -3, 0), 0.5, parent);
        _world.EntityManager.AddEntity(parent, _world.MainScene);
        Settle();

        Play();
        Frames(2);

        Assert.That(_physics.Moves(parent), Is.False, "the crate's collider is the crate's");
        Assert.That(_physics.Moves(child), Is.True);
        Assert.That(child.Transform.WorldPosition.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    [Test]
    public void DisabledChildOrCollider_IsNoPartOfTheShape()
    {
        UseSteppedPhysics();
        AddGround();
        var body = new Entity(null, "Body");
        body.Transform.Position = new Vector3<double>(0, -3, 0);
        body.AddComponent(new RigidBody());
        AddPart(body, "Hull", Vector3<double>.Zero, new Vector3<double>(0.5));
        var hiddenEntity = AddPart(body, "Hidden", new Vector3<double>(0, 3, 0), new Vector3<double>(0.5));
        hiddenEntity.IsEnabled = false;
        var hiddenCollider = AddPart(body, "Switched off", new Vector3<double>(0, 4, 0), new Vector3<double>(0.5));
        hiddenCollider.GetComponent<Collider>().IsEnabled = false;
        _world.EntityManager.AddEntity(body, _world.MainScene);
        Settle();

        Play();
        Frames(3);

        Assert.That(body.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02), "only the hull meets the ground");
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

    // The game's code turns gravity and the settings its own way; the next game starts with the editor's again.
    [Test]
    public void GravityAndSettingsSetInAGame_GoBackWhenItStops()
    {
        UseSteppedPhysics();
        var settings = new PhysicsSettings { StepRate = 90 };
        _physics.Settings = settings;
        _physics.Gravity = new Vector3F(0, 5, 0);
        Play();

        _physics.Gravity = new Vector3F(1, 0, 0);
        _physics.Settings = new PhysicsSettings { StepRate = 30 };
        _run.Stop();
        _run = null;

        Assert.That(_physics.Gravity, Is.EqualTo(new Vector3F(0, 5, 0)));
        Assert.That(_physics.Settings, Is.SameAs(settings));
    }

    // Before the transforms, so the entities it stands are placed in the same frame.
    [Test]
    public void Physics_RunsBeforeTheTransforms()
    {
        UseSteppedPhysics();

        Assert.That(_physics.Priority, Is.LessThan(new Adamantium.Engine.EntityServices.TransformService(_world).Priority));
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

    // A rigid body the game's code gives once the game plays - after the physics started - falls as one there before.
    [Test]
    public void BodyGivenDuringTheGame_Falls()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        box.RemoveComponent<RigidBody>();
        Play();
        Frames(0.2);
        Assert.That(_physics.Moves(box), Is.False);

        box.AddComponent(new RigidBody());
        Frames(3);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    // Moved by hand while it falls, the body goes on from where it was put, still falling.
    [Test]
    public void BodyMovedDuringTheGame_GoesOnFromWhereItWasPut()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        Play();
        Frames(0.3);
        var falling = _physics.GetLinearVelocity(box);

        box.Transform.Position = new Vector3<double>(5, -6, 0);
        Frame(FrameSeconds);
        var after = box.Transform.Position;
        var speed = _physics.GetLinearVelocity(box);
        Frames(3);

        Assert.That(after.X, Is.EqualTo(5).Within(1e-3));
        Assert.That(after.Y, Is.EqualTo(-6).Within(0.1), "from where it was put, not from where it fell to");
        Assert.That(speed.Y, Is.GreaterThan(falling.Y), "still falling as fast, and faster");
        Assert.That(box.Transform.Position.X, Is.EqualTo(5).Within(0.02));
        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    // Its friction changed while it falls, the body is made anew where it stands, moving as it moved: no step back to
    // where the entity was last drawn.
    [Test]
    public void BodyEditedDuringTheGame_KeepsHowItMoves()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        Play();
        Frames(0.3);
        var before = box.Transform.Position.Y;
        var falling = _physics.GetLinearVelocity(box);

        box.GetComponent<RigidBody>().Friction = 0.1f;
        Frame(FrameSeconds);

        Assert.That(box.Transform.Position.Y, Is.GreaterThan(before), "on down, not back");
        Assert.That(_physics.GetLinearVelocity(box).Y, Is.GreaterThanOrEqualTo(falling.Y));
    }

    // The middle box's density dragged along by a slider, a frame at a time: changed where it stands, its contacts
    // kept, the stack stands as it stood.
    [Test]
    public void StackedBoxsMassDraggedEachFrame_LeavesTheStackStanding()
    {
        UseSteppedPhysics();
        AddGround();
        var boxes = new Entity[5];
        for (var index = 0; index < boxes.Length; index++)
        {
            boxes[index] = AddBox("Box" + index, new Vector3<double>(0, -0.5 - index, 0), 0.5);
        }

        Play();
        Frames(3);
        var settled = boxes[^1].Transform.Position.Y;
        var middle = boxes[2].GetComponent<RigidBody>();
        for (var frame = 0; frame < 60; frame++)
        {
            middle.Density = 1000 + frame * 10;
            Frame(FrameSeconds);
        }

        Frames(1);

        Assert.That(boxes[^1].Transform.Position.Y, Is.EqualTo(settled).Within(0.005));
        Assert.That(Math.Abs(boxes[^1].Transform.Position.X) + Math.Abs(boxes[^1].Transform.Position.Z),
            Is.LessThan(0.01));
    }

    // The middle box's density and friction cleared while typing - weighing nothing, rubbing by no number - keep what
    // they were: the stack does not lose its support.
    [Test]
    public void StackedBoxsDensityClearedWhileTyping_LeavesTheStackStanding()
    {
        UseSteppedPhysics();
        AddGround();
        var boxes = new Entity[3];
        for (var index = 0; index < boxes.Length; index++)
        {
            boxes[index] = AddBox("Box" + index, new Vector3<double>(0, -0.5 - index, 0), 0.5);
        }

        Play();
        Frames(3);
        var settled = boxes[^1].Transform.Position.Y;
        var middle = boxes[1].GetComponent<RigidBody>();

        middle.Density = 0;
        middle.Friction = float.NaN;
        Frames(0.5);
        Assert.That(_physics.Moves(boxes[1]), Is.True, "while typing");
        middle.Density = 800;
        middle.Friction = 0.5f;
        Frames(1);

        Assert.That(_physics.Moves(boxes[1]), Is.True);
        Assert.That(boxes[^1].Transform.Position.Y, Is.EqualTo(settled).Within(0.005));
    }

    // A static body weighing nothing made dynamic cannot be: the physics leaves it until it is given a weight, then
    // it falls.
    [Test]
    public void WeightlessStaticBodyMadeDynamic_FallsOnceWeighed()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        var body = box.GetComponent<RigidBody>();
        body.Kind = BodyKind.Static;
        body.Density = 0;
        Play();
        Frames(0.2);

        body.Kind = BodyKind.Dynamic;
        Frames(0.5);
        Assert.That(_physics.Moves(box), Is.False);
        Assert.That(box.Transform.Position.Y, Is.EqualTo(-4));

        body.Density = 500;
        Frames(3);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    // Selected in the editor while it falls, the body falls on as it fell.
    [Test]
    public void BodySelectedDuringTheGame_FallsOn()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        Play();
        Frames(0.3);
        var before = box.Transform.Position.Y;

        box.IsSelected = true;
        Frame(FrameSeconds);

        Assert.That(box.Transform.Position.Y, Is.GreaterThan(before));
        Frames(3);
        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    // Its rigid body taken off, the entity stays where it was; disabled, too, and falls on once enabled again.
    [Test]
    public void BodyTakenOffOrDisabled_StopsAndComesBack()
    {
        UseSteppedPhysics();
        AddGround();
        var taken = AddBox("Taken", new Vector3<double>(-3, -4, 0), 0.5);
        var disabled = AddBox("Disabled", new Vector3<double>(3, -4, 0), 0.5);
        Play();
        Frames(0.3);

        taken.RemoveComponent<RigidBody>();
        disabled.IsEnabled = false;
        Settle();
        Frame(FrameSeconds);
        var takenAt = taken.Transform.Position;
        var disabledAt = disabled.Transform.Position;
        Frames(0.5);

        Assert.That(_physics.Moves(taken), Is.False);
        Assert.That(_physics.Moves(disabled), Is.False);
        Assert.That(taken.Transform.Position, Is.EqualTo(takenAt));
        Assert.That(disabled.Transform.Position, Is.EqualTo(disabledAt));

        disabled.IsEnabled = true;
        Settle();
        Frames(3);
        Assert.That(disabled.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    // A static body made dynamic while the game plays starts to fall; a collider grown makes it rest higher.
    [Test]
    public void BodyMadeDynamicAndColliderGrown_FallsAndRestsOnTheNewSize()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        box.GetComponent<RigidBody>().Kind = BodyKind.Static;
        Play();
        Frames(0.3);
        Assert.That(box.Transform.Position.Y, Is.EqualTo(-4));

        box.GetComponent<RigidBody>().Kind = BodyKind.Dynamic;
        box.GetComponent<BoxCollider>().Bounds =
            new Bounds(Vector3<double>.Zero, new Vector3<double>(1), Quaternion<double>.Identity);
        Frames(3);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-1).Within(0.02));
    }

    // A body hung under another, moved by the physics, does not keep the one above it being made anew: both sleep.
    [Test]
    public void BodyUnderAStaticBody_LetsBothSleep()
    {
        UseSteppedPhysics();
        var level = AddGround();
        var box = MakeBox("Box", new Vector3<double>(0, -3, 0), 0.5, level);
        Settle();
        Play();
        Frames(4);

        Assert.That(box.Transform.Position.Y, Is.LessThan(-0.4), "it fell onto the level");
        Assert.That(_physics.LastStep.AwakeBodies, Is.Zero);
    }

    // Asleep on the ground and lifted by hand, a body falls again rather than hanging where it was put.
    [Test]
    public void SleepingBodyLiftedByHand_FallsAgain()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -0.6, 0), 0.5);
        Play();
        Frames(3);
        Assert.That(_physics.LastStep.AwakeBodies, Is.Zero);

        box.Transform.Position = new Vector3<double>(0, -4, 0);
        Frames(3);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    // A falling body made kinematic stops where it is, not drifting on at its fall's speed.
    [Test]
    public void FallingBodyMadeKinematic_Stops()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -6, 0), 0.5);
        Play();
        Frames(0.3);

        box.GetComponent<RigidBody>().Kind = BodyKind.Kinematic;
        Frame(FrameSeconds);
        var stopped = box.Transform.Position;
        Frames(1);

        Assert.That(_physics.GetLinearVelocity(box), Is.EqualTo(Vector3F.Zero));
        Assert.That((box.Transform.Position - stopped).Length(), Is.LessThan(0.05));
    }

    // A static floor stands where its parent puts it: the parent moved down, a box lands on the floor there.
    [Test]
    public void StaticBodyUnderAMovedParent_StandsWhereItIsPut()
    {
        UseSteppedPhysics();
        var stage = new Entity(null, "Stage");
        _world.EntityManager.AddEntity(stage, _world.MainScene);
        var floor = AddPart(stage, "Floor", new Vector3<double>(0, 1, 0), new Vector3<double>(20, 1, 20));
        floor.AddComponent(new RigidBody { Kind = BodyKind.Static });
        Settle();
        var box = AddBox("Box", new Vector3<double>(0, -3, 0), 0.5);
        Play();
        Frames(0.1);

        stage.Transform.Position = new Vector3<double>(0, 2, 0);
        Frames(3);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(1.5).Within(0.02));
    }

    // A body the physics world will not take - a negative mass - is not one the physics moves.
    [Test]
    public void BodyTheWorldWillNotTake_IsNotMoved()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        box.GetComponent<RigidBody>().Mass = -1;
        Play();
        Frames(0.2);

        Assert.That(_physics.Moves(box), Is.False);
        Assert.That(box.Transform.Position.Y, Is.EqualTo(-4));
    }

    // Selecting a body of a sleeping stack changes nothing it is made of: the stack sleeps on.
    [Test]
    public void BodyOfASleepingStackSelected_LetsItSleep()
    {
        UseSteppedPhysics();
        AddGround();
        AddBox("Bottom", new Vector3<double>(0, -0.5, 0), 0.5);
        var top = AddBox("Top", new Vector3<double>(0, -1.5, 0), 0.5);
        Play();
        Frames(3);
        Assert.That(_physics.LastStep.AwakeBodies, Is.Zero);

        top.IsSelected = true;
        Frames(0.1);

        Assert.That(_physics.LastStep.AwakeBodies, Is.Zero);
    }

    // Tumbled, its parts' places read back through the turns differ in the last digits: still the same body, left
    // asleep.
    [Test]
    public void TurnedSleepingBodySelected_LetsItSleep()
    {
        UseSteppedPhysics();
        AddGround();
        var box = MakeBox("Box", new Vector3<double>(0, -2, 0), 0.5);
        box.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3F.Normalize(new Vector3F(1, 0.3f, 0.7f)), 0.8f);
        AddPart(box, "Side", new Vector3<double>(0.8, 0.2, 0.1), new Vector3<double>(0.3));
        _world.EntityManager.AddEntity(box, _world.MainScene);
        Settle();
        Play();
        Frames(6);
        Assert.That(_physics.LastStep.AwakeBodies, Is.Zero);

        box.IsSelected = true;
        Frames(0.1);

        Assert.That(_physics.LastStep.AwakeBodies, Is.Zero);
    }

    // A part given a body of its own and pushed at once leaves the body it was part of, not thrown out of its inside.
    [Test]
    public void PartGivenABodyDuringTheGame_ComesAwayGently()
    {
        UseSteppedPhysics();
        AddGround();
        var cart = AddBox("Cart", new Vector3<double>(0, -0.5, 0), 0.5);
        var wheel = AddPart(cart, "Wheel", new Vector3<double>(1, 0, 0), new Vector3<double>(0.5));
        Settle();
        Play();
        Frames(1);

        wheel.AddComponent(new RigidBody());
        _physics.SetLinearVelocity(wheel, new Vector3F(0.5f, 0, 0));
        Frames(0.5);

        Assert.That(_physics.GetLinearVelocity(wheel).Length(), Is.LessThan(1));
        Assert.That(_physics.GetLinearVelocity(cart).Length(), Is.LessThan(1));
    }

    // A body pushed by a steady force, made kinematic: the force goes, and the physics steps on.
    [Test]
    public void PushedBodyMadeKinematic_KeepsThePhysicsStepping()
    {
        UseSteppedPhysics();
        AddGround();
        var pushed = AddBox("Pushed", new Vector3<double>(-3, -0.5, 0), 0.5);
        var falling = AddBox("Falling", new Vector3<double>(3, -4, 0), 0.5);
        Play();
        _physics.SetForce(pushed, new Vector3F(10, 0, 0));
        Frames(0.2);

        pushed.GetComponent<RigidBody>().Kind = BodyKind.Kinematic;
        Frames(3);

        Assert.That(falling.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    // A falling body made static stands where it is drawn: a box dropped on it rests right on top.
    [Test]
    public void FallingBodyMadeStatic_StandsWhereItIsDrawn()
    {
        UseSteppedPhysics();
        AddGround();
        var held = AddBox("Held", new Vector3<double>(0, -6, 0), 0.5);
        Play();
        Frames(0.3);

        held.GetComponent<RigidBody>().Kind = BodyKind.Static;
        Frame(FrameSeconds);
        var at = held.Transform.Position.Y;
        var dropped = AddBox("Dropped", new Vector3<double>(0, at - 3, 0), 0.5);
        Frames(3);

        Assert.That(held.Transform.Position.Y, Is.EqualTo(at));
        Assert.That(dropped.Transform.Position.Y, Is.EqualTo(at - 1).Within(0.02));
    }

    // A rigid body the game's code gives can be pushed in the same frame, before the physics looks again.
    [Test]
    public void BodyGivenDuringTheGame_CanBePushedAtOnce()
    {
        UseSteppedPhysics();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        box.RemoveComponent<RigidBody>();
        Play();
        Frames(0.1);

        box.AddComponent(new RigidBody { GravityScale = 0 });
        _physics.SetLinearVelocity(box, new Vector3F(2, 0, 0));
        Frames(0.5);

        Assert.That(box.Transform.Position.X, Is.EqualTo(1).Within(0.05));
    }

    // Only its collider grown while it rests, the body rests on the new size.
    [Test]
    public void ColliderGrownDuringTheGame_RestsOnTheNewSize()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -0.5, 0), 0.5);
        Play();
        Frames(1);

        box.GetComponent<BoxCollider>().Bounds =
            new Bounds(Vector3<double>.Zero, new Vector3<double>(1), Quaternion<double>.Identity);
        Frames(3);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-1).Within(0.02));
    }

    // An entity taken out of the world while it falls leaves the physics with it.
    [Test]
    public void EntityTakenOutDuringTheGame_LeavesThePhysics()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        Play();
        Frames(0.3);

        _world.EntityManager.RemoveEntity(box);
        Settle();
        Frame(FrameSeconds);

        Assert.That(_physics.Moves(box), Is.False);
    }

    // A body with no collider is left; a part with one hung under it later makes it a body.
    [Test]
    public void PartWithAColliderHungUnderABodyLater_MakesItOne()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        box.RemoveComponent<BoxCollider>();
        Play();
        Frames(0.3);
        Assert.That(_physics.Moves(box), Is.False);

        AddPart(box, "Part", Vector3<double>.Zero, new Vector3<double>(0.5));
        Settle();
        Frames(3);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    // What watches the transform - an inspector - is told of the last place the body is stood at, not left at the start.
    [Test]
    public void BoxLanding_IsToldToWhatWatchesItsTransform()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -4, 0), 0.5);
        var lastTold = double.NaN;
        box.Transform.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Transform.Position))
                lastTold = box.Transform.Position.Y;
        };
        Play();
        Frames(3);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
        Assert.That(lastTold, Is.EqualTo(box.Transform.Position.Y));
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
        _physics.SetForce(box, new Vector3F(0, -1000 * 9.81f, 0));
        for (var frame = 0; frame < 8; frame++)
        {
            Frame(0.5);
        }

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-3).Within(0.05));
    }

    [Test]
    public void ForceSetToZero_StopsPushing()
    {
        UseSteppedPhysics();
        _physics.Gravity = Vector3F.Zero;
        var box = AddBox("Pushed", Vector3<double>.Zero, 0.5);
        Play();

        _physics.SetForce(box, new Vector3F(1000, 0, 0));
        Frames(0.5);
        _physics.SetForce(box, Vector3F.Zero);
        Frames(0.1);
        var speed = _physics.GetLinearVelocity(box).X;
        Frames(0.5);

        Assert.That(speed, Is.EqualTo(0.5f).Within(0.02f));
        Assert.That(_physics.GetLinearVelocity(box).X, Is.EqualTo(speed).Within(1e-5f));
    }

    [Test]
    public void SmallForce_IsFeltThoughTheBodyGoesSlowerThanSleep()
    {
        UseSteppedPhysics();
        _physics.Gravity = Vector3F.Zero;
        var box = AddBox("Crawling", Vector3<double>.Zero, 0.5);
        Play();

        _physics.SetForce(box, new Vector3F(10, 0, 0));
        Frames(3);

        Assert.That(_physics.GetLinearVelocity(box).X, Is.EqualTo(0.03f).Within(0.002f));
    }

    [Test]
    public void ForceTooStrongForTheBody_IsDroppedAndTheGameGoesOn()
    {
        UseSteppedPhysics();
        _physics.Gravity = Vector3F.Zero;
        var box = MakeBox("Light", Vector3<double>.Zero, 0.5);
        box.GetComponent<RigidBody>().Mass = 0.001f;
        _world.EntityManager.AddEntity(box, _world.MainScene);
        Settle();
        Play();

        for (var frame = 0; frame < 3; frame++)
        {
            _physics.SetForce(box, new Vector3F(3e38f, 0, 0));
            Frame(FrameSeconds);
        }

        Assert.That(_physics.GetLinearVelocity(box).X, Is.Zero);
        _physics.SetForce(box, new Vector3F(0.001f, 0, 0));
        Frames(0.5);

        Assert.That(_physics.GetLinearVelocity(box).X, Is.EqualTo(0.5f).Within(0.02f));
    }

    [Test]
    public void ForceSetInAFrameShorterThanAStep_IsKept()
    {
        UseSteppedPhysics();
        _physics.Gravity = Vector3F.Zero;
        var box = AddBox("Pushed", Vector3<double>.Zero, 0.5);
        Play();

        _physics.SetForce(box, new Vector3F(1000, 0, 0));
        Frame(1.0 / 480);
        Frame(1.0 / 480);
        Frames(0.5);

        Assert.That(_physics.GetLinearVelocity(box).X, Is.EqualTo(0.5f).Within(0.02f));
    }

    [Test]
    public void ForceOfAStoppedGame_IsNotFeltInTheNext()
    {
        UseSteppedPhysics();
        _physics.Gravity = Vector3F.Zero;
        var box = AddBox("Pushed", Vector3<double>.Zero, 0.5);
        Play();
        _physics.SetForce(box, new Vector3F(1000, 0, 0));
        Frames(0.2);
        _run.Stop();

        Play();
        Frames(0.2);

        Assert.That(_physics.GetLinearVelocity(box).X, Is.Zero);
    }

    [Test]
    public void ForceOnAStaticBody_IsRefused()
    {
        UseSteppedPhysics();
        var ground = AddGround();
        var box = AddBox("Box", new Vector3<double>(0, -3, 0), 0.5);
        Play();

        Assert.Throws<InvalidOperationException>(() => _physics.SetForce(ground, Vector3F.UnitX));
        Assert.Throws<ArgumentOutOfRangeException>(() => _physics.SetForce(box, new Vector3F(float.NaN, 0, 0)));
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
    public void GroundUnderAMirroredParent_StillHoldsWhatFalls()
    {
        UseSteppedPhysics();
        var level = new Entity(null, "Mirrored level");
        level.Transform.ScaleFactor = new Vector3F(-1, 1, 1);
        _world.EntityManager.AddEntity(level, _world.MainScene);
        var ground = new Entity(level, "Ground");
        ground.Transform.Position = new Vector3<double>(0, 1, 0);
        ground.AddComponent(new BoxCollider
        {
            Bounds = new Bounds(Vector3<double>.Zero, new Vector3<double>(20, 1, 20), Quaternion<double>.Identity)
        });
        ground.AddComponent(new RigidBody { Kind = BodyKind.Static });
        Settle();
        var box = AddBox("Box", new Vector3<double>(0, -3, 0), 0.5);

        Play();
        Frames(3);

        Assert.That(_physics.Moves(ground), Is.True);
        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    [Test]
    public void MovingBodyUnderAStretchedParent_IsLeftOut()
    {
        UseSteppedPhysics();
        var parent = new Entity(null, "Stretched");
        parent.Transform.ScaleFactor = new Vector3F(4, 1, 1);
        _world.EntityManager.AddEntity(parent, _world.MainScene);
        var box = MakeBox("Upright", Vector3<double>.Zero, 0.5, parent);
        Settle();

        Play();

        Assert.That(_physics.Moves(box), Is.False);
    }

    [Test]
    public void ParentStretchedInTheGame_StopsStandingTheChild()
    {
        UseSteppedPhysics();
        AddGround();
        var parent = new Entity(null, "Parent");
        _world.EntityManager.AddEntity(parent, _world.MainScene);
        var box = MakeBox("Child", new Vector3<double>(0, -0.5, 0), 0.5, parent);
        Settle();
        Play();
        Frames(1);
        var kept = box.Transform.State;

        parent.Transform.ScaleFactor = new Vector3F(4, 1, 1);
        Frames(0.2);

        Assert.That(_physics.Moves(box), Is.True);
        Assert.That(box.Transform.State, Is.EqualTo(kept));
    }

    [TestCase(1.0 / 60)]
    [TestCase(1.0 / 144)]
    public void ForceAsHeavyAsTheBody_HoversItWithoutDrift(double frameSeconds)
    {
        UseSteppedPhysics();
        var box = AddBox("Hovering", new Vector3<double>(0, -3, 0), 0.5);
        Play();
        _physics.SetForce(box, new Vector3F(0, -1000 * 9.81f, 0));
        var frames = (int)Math.Round(3 / frameSeconds);
        for (var frame = 0; frame < frames; frame++)
        {
            Frame(frameSeconds);
        }

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-3).Within(0.01));
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

    [Test]
    public void OwnThread_FeelsTheForceSet()
    {
        _physics = new PhysicsService(_world);
        _world.AddService(_physics);
        _physics.Gravity = Vector3F.Zero;
        Settle();
        var box = AddBox("Pushed", Vector3<double>.Zero, 0.5);

        Play();
        _physics.SetForce(box, new Vector3F(1000, 0, 0));
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5) && _physics.GetLinearVelocity(box).X < 0.2f)
        {
            Thread.Sleep(10);
            Frame(0.01);
        }

        Assert.That(_physics.GetLinearVelocity(box).X, Is.GreaterThanOrEqualTo(0.2f));
        Assert.That(box.Transform.Position.X, Is.GreaterThan(0));
    }

    private static float MeasureSpeedAfterSteadyForce(double frameSeconds)
    {
        var tests = new PhysicsServiceTests();
        tests.MakeWorld();
        tests.UseSteppedPhysics();
        tests._physics.Gravity = Vector3F.Zero;
        var box = tests.AddBox("Pushed", Vector3<double>.Zero, 0.5);
        tests.Play();
        tests._physics.SetForce(box, new Vector3F(1000, 0, 0));
        var frames = (int)Math.Round(1 / frameSeconds);
        for (var frame = 0; frame < frames; frame++)
        {
            tests.Frame(frameSeconds);
        }

        tests._physics.SetForce(box, Vector3F.Zero);
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

    private Entity AddGround()
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
        return ground;
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

    private static Entity AddPart(Entity owner, string name, Vector3<double> position, Vector3<double> half)
    {
        var part = new Entity(owner, name);
        part.Transform.Position = position;
        part.AddComponent(new BoxCollider { Bounds = new Bounds(Vector3<double>.Zero, half, Quaternion<double>.Identity) });
        return part;
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
