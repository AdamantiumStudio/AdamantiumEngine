using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.ECSTests;

/// <summary>A body's computed mass: its own mass, or its density times its colliders' volume, weighed anew when what it
/// is made of changes.</summary>
[TestFixture]
public class ComputedMassTests
{
    [Test]
    public void ACubeMeterOfWater_WeighsATon_AndAMassSetIsTaken()
    {
        var body = Body(out var box);

        var fromDensity = body.ComputedMass;
        body.Mass = 50;
        var set = body.ComputedMass;
        body.Mass = 0;

        Assert.Multiple(() =>
        {
            Assert.That(fromDensity, Is.EqualTo(1000).Within(1e-3));
            Assert.That(set, Is.EqualTo(50));
            Assert.That(body.ComputedMass, Is.EqualTo(1000).Within(1e-3));
            Assert.That(box.IsFitted, Is.False);
        });
    }

    [Test]
    public void AColliderResized_OrADensityChanged_WeighsAnew_AndSaysSo()
    {
        var body = Body(out var box);
        var said = new List<string>();
        body.PropertyChanged += (_, args) => said.Add(args.PropertyName);

        box.Size = new Vector3F(2, 1, 1);
        var resized = body.ComputedMass;
        body.Density = 500;

        Assert.Multiple(() =>
        {
            Assert.That(resized, Is.EqualTo(2000).Within(1e-3));
            Assert.That(body.ComputedMass, Is.EqualTo(1000).Within(1e-3));
            Assert.That(said, Does.Contain(nameof(RigidBody.ComputedMass)));
        });
    }

    [Test]
    public void PartsBelow_WeighOnTheBody_ButNotOnesWithABodyOfTheirOwn_OrTurnedOff()
    {
        var body = Body(out _);
        var part = Part(body.Owner, new Vector3F(1));
        var ownBody = Part(body.Owner, new Vector3F(1));
        ownBody.AddComponent(new RigidBody());
        var withPart = body.ComputedMass;

        part.IsEnabled = false;
        var partOff = body.ComputedMass;
        part.IsEnabled = true;
        part.GetComponent<BoxCollider>().IsEnabled = false;

        Assert.Multiple(() =>
        {
            Assert.That(withPart, Is.EqualTo(2000).Within(1e-3));
            Assert.That(partOff, Is.EqualTo(1000).Within(1e-3));
            Assert.That(body.ComputedMass, Is.EqualTo(1000).Within(1e-3));
        });
    }

    [Test]
    public void AScale_OnTheBodyOrAboveIt_GrowsItsVolume()
    {
        var parent = new Entity(null, "Parent");
        var body = Body(out _, parent);

        body.Owner.Transform.BaseScale = new Vector3F(2, 1, 1);
        var scaled = body.ComputedMass;
        parent.Transform.BaseScale = new Vector3F(1, 3, 1);

        Assert.Multiple(() =>
        {
            Assert.That(scaled, Is.EqualTo(2000).Within(1e-3));
            Assert.That(body.ComputedMass, Is.EqualTo(6000).Within(1e-2));
        });
    }

    [Test]
    public void APartHungUnderAnotherBody_WeighsOnThatOneInstead()
    {
        var first = Body(out _);
        var second = Body(out _);
        var part = Part(first.Owner, new Vector3F(1));
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());

        world.EntityManager.SetOwner(part, second.Owner);

        Assert.Multiple(() =>
        {
            Assert.That(first.ComputedMass, Is.EqualTo(1000).Within(1e-3));
            Assert.That(second.ComputedMass, Is.EqualTo(2000).Within(1e-3));
        });
    }

    [Test]
    public void APartTakenOff_WeighsNoMore()
    {
        var body = Body(out _);
        var part = Part(body.Owner, new Vector3F(1));
        var world = new EntityWorld(new AdamantiumDependencyContainer(), new Satellites());
        var withPart = body.ComputedMass;

        world.EntityManager.RemoveOwner(part);

        Assert.Multiple(() =>
        {
            Assert.That(withPart, Is.EqualTo(2000).Within(1e-3));
            Assert.That(body.ComputedMass, Is.EqualTo(1000).Within(1e-3));
        });
    }

    // A capsule standing along its part's Y, the part turned a quarter about Z under a body stretched three times
    // along Y: its length lies across the stretch, its radius grows threefold.
    [Test]
    public void APartTurnedUnderAnUnevenScale_IsMeasuredAlongItsOwnAxes()
    {
        var entity = new Entity(null, "Body");
        var body = new RigidBody();
        entity.AddComponent(body);
        entity.Transform.BaseScale = new Vector3F(1, 3, 1);
        var part = new Entity(entity, "Part");
        part.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 2);
        part.AddComponent(new CapsuleCollider { Radius = 0.4f, Height = 2 });

        const double radius = 1.2;
        const double core = 1.2;
        var expected = 1000 * (Math.PI * radius * radius * core + 4.0 / 3.0 * Math.PI * radius * radius * radius);
        Assert.That(body.ComputedMass, Is.EqualTo(expected).Within(expected * 1e-4));
    }

    // Turned 45 degrees under a threefold stretch, a part is skewed: the physics carries its box in as the hull of its
    // corners, three times the box's space; a ball skewed is an egg, and is left out.
    [Test]
    public void ASkewedPartsBox_WeighsAsItsCarriedHull_AndABallNothing()
    {
        var body = Body(out _);
        body.Owner.Transform.BaseScale = new Vector3F(1, 3, 1);
        var part = Part(body.Owner, new Vector3F(1));
        part.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, MathF.PI / 4);
        part.AddComponent(new SphereCollider { Radius = 1 });

        Assert.That(body.ComputedMass, Is.EqualTo(6000).Within(1e-1));
    }

    [Test]
    public void SayingTheMassChanged_DoesNotCountAsTheEntitysChange()
    {
        var body = Body(out _);
        _ = body.ComputedMass;
        var before = body.Owner.ChangeCount;

        body.Density = 300;

        Assert.That(body.Owner.ChangeCount, Is.EqualTo(before + 1), "the density's change alone");
    }

    [Test]
    public void AMeshCollider_AddsNoWeight()
    {
        var body = Body(out _);
        body.Owner.AddComponent(new MeshCollider());

        Assert.That(body.ComputedMass, Is.EqualTo(1000).Within(1e-3));
    }

    private static RigidBody Body(out BoxCollider box, Entity owner = null)
    {
        var entity = new Entity(owner, "Body");
        var body = new RigidBody();
        entity.AddComponent(body);
        box = new BoxCollider { Size = new Vector3F(1) };
        entity.AddComponent(box);
        return body;
    }

    private static Entity Part(Entity owner, Vector3F size)
    {
        var part = new Entity(owner, "Part");
        part.AddComponent(new BoxCollider { Size = size });
        return part;
    }
}
