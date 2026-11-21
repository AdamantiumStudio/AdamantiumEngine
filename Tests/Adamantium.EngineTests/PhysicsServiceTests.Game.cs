using System;
using System.Collections.Generic;
using Adamantium.DarkMatter;
using Adamantium.DarkMatter.Shapes;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Physics;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The game's own work with the physics through the service: queries and contacts told by entities and
/// colliders, and the chains of a body and of the world.</summary>
public partial class PhysicsServiceTests
{
    [Test]
    public void TheWorldsPhysics_IsItsService()
    {
        UseSteppedPhysics();

        Assert.That(_world.Physics(), Is.SameAs(_physics));
    }

    // A box on the ground, a ray from above: the box first, then the ground past it.
    [Test]
    public void ARay_MeetsTheEntityAndItsCollider_AndPassesTheOneExcepted()
    {
        UseSteppedPhysics();
        var ground = AddGround();
        var box = AddBox("Box", new WorldPosition(0, -0.5, 0), 0.5);
        Play();
        Frame(FrameSeconds);
        var from = new WorldPosition(0, -10, 0);

        var metBox = _physics.Query().RayCast(from, Vector3F.UnitY, 20, out var first);
        var metGround = _physics.Query().Except(box).RayCast(from, Vector3F.UnitY, 20, out var past);
        var hits = new EntityRayHit[4];
        var count = _physics.Query().RayCast(from, Vector3F.UnitY, 20, hits);

        Assert.Multiple(() =>
        {
            Assert.That(metBox, Is.True);
            Assert.That(first.Entity, Is.SameAs(box));
            Assert.That(first.Collider, Is.SameAs(box.GetComponent<BoxCollider>()));
            Assert.That(first.Distance, Is.EqualTo(9).Within(0.02f));
            Assert.That(metGround, Is.True);
            Assert.That(past.Entity, Is.SameAs(ground));
            Assert.That(past.Distance, Is.EqualTo(10).Within(0.02f));
            Assert.That(count, Is.EqualTo(2));
            Assert.That((Entity[])[hits[0].Entity, hits[1].Entity], Is.EqualTo((Entity[])[box, ground]));
        });
    }

    // A cart of two boxes under it: the ray meets the cart, by the collider of the box it struck.
    [Test]
    public void ARayMeetingAPart_TellsTheBodysEntityAndThePartsCollider()
    {
        UseSteppedPhysics();
        var cart = new Entity(null, "Cart");
        cart.AddComponent(new RigidBody { Kind = BodyKind.Static });
        AddPart(cart, "Left", new WorldPosition(-1, 0, 0), new Vector3<double>(0.5));
        var right = AddPart(cart, "Right", new WorldPosition(1, 0, 0), new Vector3<double>(0.5));
        _world.EntityManager.AddEntity(cart, _world.MainScene);
        Settle();
        Play();
        Frame(FrameSeconds);

        var met = _physics.RayCast(new WorldPosition(1, -10, 0), Vector3F.UnitY, 20, out var hit);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(hit.Entity, Is.SameAs(cart));
            Assert.That(hit.Collider, Is.SameAs(right.GetComponent<BoxCollider>()));
        });
    }

    // The collider swapped for one alike during the game: the same shape, the new collider told.
    [Test]
    public void AColliderSwappedForOneAlike_IsTheOneTold()
    {
        UseSteppedPhysics();
        var box = MakeBox("Box", new WorldPosition(0, -0.5, 0), 0.5);
        box.GetComponent<RigidBody>().Kind = BodyKind.Static;
        _world.EntityManager.AddEntity(box, _world.MainScene);
        Settle();
        Play();
        Frame(FrameSeconds);

        box.RemoveComponent<BoxCollider>();
        var swapped = new BoxCollider { Size = new Vector3F(1) };
        box.AddComponent(swapped);
        Settle();
        Frames(0.1);
        _physics.RayCast(new WorldPosition(0, -10, 0), Vector3F.UnitY, 20, out var hit);

        Assert.That(hit.Collider, Is.SameAs(swapped));
    }

    [Test]
    public void AnOverlap_TellsTheEntitiesWhoseBodiesItTouches()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new WorldPosition(0, -0.5, 0), 0.5);
        var far = AddBox("Far", new WorldPosition(5, -0.5, 0), 0.5);
        Play();
        Frame(FrameSeconds);

        var touched = new Entity[4];
        var count = _physics.Query().Overlap(new SphereCollisionShape(0.3f),
            new Pose(new WorldPosition(0, -0.5, 0), QuaternionF.Identity), touched);

        Assert.That(touched[..count], Is.EqualTo((Entity[])[box]));
        Assert.That(touched, Does.Not.Contain(far));
    }

    // A box landing, then taken out: its touch of the ground begins, then ends, both told by the entities.
    [Test]
    public void ABoxLandingAndTakenOut_IsToldToBeginAndEndTouchingTheGround()
    {
        UseSteppedPhysics();
        var ground = AddGround();
        var box = AddBox("Box", new WorldPosition(0, -1.5, 0), 0.5);
        Play();
        var told = new List<EntityContactEventArgs>();
        for (var frame = 0; frame < 60; frame++)
        {
            Frame(FrameSeconds);
            told.AddRange(_physics.Contacts.ToArray());
        }

        _world.EntityManager.RemoveEntity(box);
        Settle();
        Frame(FrameSeconds);
        told.AddRange(_physics.Contacts.ToArray());

        var began = told.Find(contact => contact.Change == ContactChange.Began);
        var ended = told.Find(contact => contact.Change == ContactChange.Ended);
        Assert.Multiple(() =>
        {
            Assert.That(began.Involves(box, out var other) && other == ground, Is.True);
            Assert.That(began.Involves(box, out _) && began.ApproachSpeed > 3, Is.True, "fell a meter: 4.4 m/s");
            Assert.That((Collider[])[began.FirstCollider, began.SecondCollider],
                Is.EquivalentTo((Collider[])[box.GetComponent<BoxCollider>(), ground.GetComponent<BoxCollider>()]));
            Assert.That(ended.Involves(box, out var left) && left == ground, Is.True);
        });
    }

    // A cart resting on two boxes loses one: the touch of the box taken off ends, told by its own collider.
    [Test]
    public void APartTakenOffARestingBody_EndsItsTouch_ToldByItsCollider()
    {
        UseSteppedPhysics();
        AddGround();
        var cart = new Entity(null, "Cart");
        cart.Transform.Position = new WorldPosition(0, -0.5, 0);
        cart.AddComponent(new RigidBody());
        var left = AddPart(cart, "Left", new WorldPosition(-1, 0, 0), new Vector3<double>(0.5));
        AddPart(cart, "Right", new WorldPosition(1, 0, 0), new Vector3<double>(0.5));
        _world.EntityManager.AddEntity(cart, _world.MainScene);
        Settle();
        Play();
        Frames(0.5);
        var leftCollider = left.GetComponent<BoxCollider>();

        _world.EntityManager.RemoveEntity(left);
        Settle();
        var told = new List<EntityContactEventArgs>();
        for (var frame = 0; frame < 3; frame++)
        {
            Frame(FrameSeconds);
            told.AddRange(_physics.Contacts.ToArray());
        }

        Assert.That(told.Exists(contact => contact.Change == ContactChange.Ended
                                           && (contact.FirstCollider == leftCollider
                                               || contact.SecondCollider == leftCollider)), Is.True);
    }

    [Test]
    public void ABodysChain_PushesAndReadsAsItsMethodsDo()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new WorldPosition(0, -0.5, 0), 0.5);
        Play();
        Frames(0.5);

        var body = _physics.Body(box).SetLinearVelocity(new Vector3F(2, 0, 0)).ApplyImpulse(new Vector3F(0, 0, 1000));
        Frame(FrameSeconds);

        var mass = body.Mass;
        var velocity = _physics.Body(box).LinearVelocity;
        Assert.Multiple(() =>
        {
            Assert.That(mass, Is.EqualTo(1000).Within(1));
            Assert.That(velocity.X, Is.EqualTo(2).Within(0.2f));
            Assert.That(velocity.Z, Is.EqualTo(1).Within(0.1f));
            Assert.That(_physics.Body(box).IsSleeping, Is.False);
        });
    }

    [Test]
    public void OnlyAKinematicBody_IsMovedAsKinematic()
    {
        UseSteppedPhysics();
        var box = AddBox("Box", new WorldPosition(0, -3, 0), 0.5);
        var lift = MakeBox("Lift", new WorldPosition(5, -3, 0), 0.5);
        lift.GetComponent<RigidBody>().Kind = BodyKind.Kinematic;
        _world.EntityManager.AddEntity(lift, _world.MainScene);
        Settle();
        Play();
        Frame(FrameSeconds);

        _physics.Body(lift).MoveKinematic(new Pose(new WorldPosition(5, -4, 0), QuaternionF.Identity));
        Frames(0.1);

        Assert.That(lift.Transform.Position.Y, Is.EqualTo(-4).Within(1e-3));
        Assert.Throws<InvalidOperationException>(() =>
            _physics.MoveKinematic(box, new Pose(WorldPosition.Zero, QuaternionF.Identity)));
    }

    // A blast beside a box resting on the ground pushes it away; another past the radius stays.
    [Test]
    public void TheWorldsChain_BlastsTheBodiesNear()
    {
        UseSteppedPhysics();
        AddGround();
        var near = AddBox("Near", new WorldPosition(2, -0.5, 0), 0.5);
        var far = AddBox("Far", new WorldPosition(20, -0.5, 0), 0.5);
        Play();
        Frames(0.5);

        _physics.World().ApplyRadialImpulse(new WorldPosition(0, -0.5, 0), radius: 10, strength: 5000);
        Frame(FrameSeconds);

        Assert.Multiple(() =>
        {
            Assert.That(_physics.GetLinearVelocity(near).X, Is.GreaterThan(3));
            Assert.That(_physics.GetLinearVelocity(far).Length(), Is.LessThan(0.05f));
        });
    }

    [Test]
    public void WithNoGame_QueriesMeetNothing_AndTheWorldIsNotActedOn()
    {
        UseSteppedPhysics();
        AddBox("Box", new WorldPosition(0, -0.5, 0), 0.5);

        var met = _physics.Query().RayCast(new WorldPosition(0, -10, 0), Vector3F.UnitY, 20, out _);

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.False);
            Assert.That(_physics.Contacts.IsEmpty, Is.True);
            Assert.Throws<InvalidOperationException>(() => _physics.World().ApplyRadialImpulse(WorldPosition.Zero, 1, 1));
        });
    }

    [Test]
    public void QueriesAndContactsWhileTheGamePlays_DoNotAllocate()
    {
        UseSteppedPhysics();
        var ground = AddGround();
        AddBox("Box", new WorldPosition(0, -0.5, 0), 0.5);
        Play();
        var hits = new EntityRayHit[100];
        var touched = new Entity[100];
        var sphere = new SphereCollisionShape(0.3f);
        var pose = new Pose(new WorldPosition(0, -0.5, 0), QuaternionF.Identity);
        var from = new WorldPosition(0, -10, 0);
        for (var warmUp = 0; warmUp < 30; warmUp++)
        {
            Query();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var round = 0; round < 100; round++)
        {
            Query();
        }

        Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);

        void Query()
        {
            _physics.Query().Except(ground).RayCast(from, Vector3F.UnitY, 20, out _);
            _physics.Query().RayCast(from, Vector3F.UnitY, 20, hits);
            _physics.Query().Overlap(sphere, pose, touched);
            foreach (var contact in _physics.Contacts)
            {
                contact.Involves(ground, out _);
            }
        }
    }
}
