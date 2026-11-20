using System;
using System.Collections.Generic;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>A body changed where it stands - its kind, shape, weight and material - without being taken out and added
/// again.</summary>
[TestFixture]
public class BodyChangeTests
{
    [Test]
    public void StaticBodyMadeDynamic_Falls()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(0.5f, 0.5f, 0.5f), Position = new WorldPosition(0, 3, 0)
        });
        scene.Run(0.5f);
        Assert.That(scene.World.GetPose(box).Position.Y, Is.EqualTo(3));

        scene.World.SetKind(box, BodyKind.Dynamic);
        scene.Run(3);

        Assert.That(scene.World.GetPose(box).Position.Y, Is.EqualTo(0.5f).Within(0.02f));
    }

    // A falling body made static stops from rest where it is, and carries what falls on it.
    [Test]
    public void FallingBodyMadeStatic_StopsAndCarries()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var held = scene.AddBox(new Vector3F(0, 6, 0), new Vector3F(0.5f));
        scene.Run(0.3f);

        scene.World.SetKind(held, BodyKind.Static);
        var at = scene.World.GetPose(held).Position.Y;
        var dropped = scene.AddBox(new Vector3F(0, (float)at + 3, 0), new Vector3F(0.5f));
        scene.Run(3);

        Assert.That(scene.World.GetLinearVelocity(held), Is.EqualTo(Vector3F.Zero));
        Assert.That(scene.World.GetPose(held).Position.Y, Is.EqualTo(at));
        Assert.That(scene.World.GetPose(dropped).Position.Y, Is.EqualTo(at + 1).Within(0.02f));
    }

    // A compound whose center of mass is off its origin, made dynamic, keeps its origin where it stood and falls on
    // its parts.
    [Test]
    public void CompoundMadeDynamic_KeepsItsOriginAndRestsOnItsParts()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var shape = new CompoundCollisionShape([
            new CompoundPart(new BoxCollisionShape(0.5f, 0.5f, 0.5f), new LocalPose(new Vector3F(1, 0, 0))),
            new CompoundPart(new BoxCollisionShape(0.5f, 0.5f, 0.5f), new LocalPose(new Vector3F(2, 0, 0)))
        ]);
        var body = scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = shape, Position = new WorldPosition(0, 3, 0)
        });

        scene.World.SetKind(body, BodyKind.Dynamic);
        var origin = scene.World.GetPose(body).Position;
        scene.Run(3);

        Assert.That(origin, Is.EqualTo(new WorldPosition(0, 3, 0)));
        Assert.That(scene.World.GetLocalCenterOfMass(body).X, Is.EqualTo(1.5f).Within(1e-4f));
        Assert.That(scene.World.GetPose(body).Position.Y, Is.EqualTo(0.5f).Within(0.02f));
        Assert.That(scene.World.GetPose(body).Position.X, Is.EqualTo(0).Within(0.02f));
    }

    // A sleeping stack's bottom made fifty times heavier: the stack still stands.
    [Test]
    public void SleepingStacksBottomMadeHeavier_StillStands()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var bottom = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        var top = scene.AddBox(new Vector3F(0, 1.5f, 0), new Vector3F(0.5f));
        scene.Run(3);

        scene.World.SetMass(bottom, 50, 0);
        scene.Run(2);

        Assert.That(scene.World.GetPose(bottom).Position.Y, Is.EqualTo(0.5f).Within(0.02f));
        Assert.That(scene.World.GetPose(top).Position.Y, Is.EqualTo(1.5f).Within(0.02f));
    }

    // A resting box given a larger shape rests on it.
    [Test]
    public void RestingBoxGivenALargerShape_RestsOnIt()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(2);

        scene.World.SetShape(box, new BoxCollisionShape(1, 1, 1));
        scene.Run(3);

        Assert.That(scene.World.GetPose(box).Position.Y, Is.EqualTo(1).Within(0.02f));
    }

    // A box resting on a slope by its friction slides once its friction is gone: the contact it has uses it at once.
    [Test]
    public void BoxOnASlopeLosingItsFriction_Slides()
    {
        var scene = new SceneBuilder();
        var slope = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, 20 * MathF.PI / 180);
        scene.AddGround(0.6f, slope);
        var box = scene.AddBox(Vector3F.Transform(new Vector3F(0, 0.5f, 0), slope), new Vector3F(0.5f), 0.6f,
            rotation: slope);
        scene.Run(2);
        var resting = scene.World.GetPose(box).Position;

        scene.World.SetMaterial(box, 0, 0);
        scene.Run(1);

        Assert.That((scene.World.GetPose(box).Position - resting).Length(), Is.GreaterThan(0.5f));
    }

    // A falling body freed from gravity flies on at the speed it had.
    [Test]
    public void FallingBodyFreedFromGravity_FliesOn()
    {
        var scene = new SceneBuilder();
        var box = scene.AddBox(new Vector3F(0, 50, 0), new Vector3F(0.5f));
        scene.Run(0.5f);

        scene.World.SetGravityScale(box, 0);
        var speed = scene.World.GetLinearVelocity(box);
        scene.Run(0.5f);

        Assert.That((scene.World.GetLinearVelocity(box) - speed).Length(), Is.LessThan(1e-3f));
    }

    // A change the body cannot take - a dynamic body weighing nothing - leaves it as it was.
    [Test]
    public void ChangeTheBodyCannotTake_LeavesItAsItWas()
    {
        var scene = new SceneBuilder();
        var box = scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(0.5f, 0.5f, 0.5f), Mass = 0, Density = 0
        });

        Assert.Throws<ArgumentException>(() => scene.World.SetKind(box, BodyKind.Dynamic));
        Assert.That(scene.World.GetKind(box), Is.EqualTo(BodyKind.Static));
    }

    // Weighing nothing, a static body made dynamic and given a mass at once takes both: the change is checked whole.
    [Test]
    public void StaticBodyMadeDynamicWithAMassAtOnce_TakesBoth()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(0.5f, 0.5f, 0.5f), Position = new WorldPosition(0, 3, 0),
            Mass = 0, Density = 0
        });

        scene.World.Change(box, new BodyDescription
        {
            Kind = BodyKind.Dynamic, Shape = new BoxCollisionShape(1, 1, 1), Mass = 5
        }, changesShape: false);
        scene.Run(3);

        Assert.That(scene.World.GetKind(box), Is.EqualTo(BodyKind.Dynamic));
        Assert.That(scene.World.GetPose(box).Position.Y, Is.EqualTo(0.5f).Within(0.02f), "its own shape kept");
    }

    // A change it cannot take as a whole - dynamic, a new shape, weighing nothing - leaves the body as it was.
    [Test]
    public void ChangeItCannotTakeAsAWhole_LeavesItAsItWas()
    {
        var scene = new SceneBuilder();
        var box = scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(0.5f, 0.5f, 0.5f), Friction = 0.3f
        });

        Assert.Throws<ArgumentException>(() => scene.World.Change(box, new BodyDescription
        {
            Kind = BodyKind.Dynamic, Shape = new BoxCollisionShape(2, 2, 2), Mass = 0, Density = 0, Friction = 0.9f
        }, changesShape: true));

        Assert.That(scene.World.GetKind(box), Is.EqualTo(BodyKind.Static));
        Assert.That(scene.World.GetBounds(box).Maximum.X, Is.EqualTo(0.5f).Within(0.1f));
    }

    // A static body's weight moves nothing and is not looked at - even one no body could have - until it is made
    // dynamic, which then refuses it and leaves the body static.
    [Test]
    public void StaticBodysWeight_IsLookedAtOnlyWhenItIsMadeDynamic()
    {
        var scene = new SceneBuilder();
        var box = scene.World.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(0.5f, 0.5f, 0.5f), Mass = float.NaN, Density = -1
        });

        scene.World.SetMass(box, float.NaN, -2);

        Assert.Throws<ArgumentOutOfRangeException>(() => scene.World.SetKind(box, BodyKind.Dynamic));
        Assert.That(scene.World.GetKind(box), Is.EqualTo(BodyKind.Static));
    }

    // A density beside a mass weighs nothing: changed, the sleeping body is left asleep.
    [Test]
    public void DensityBesideAMassChanged_LeavesTheBodyAsleep()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.World.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(0.5f, 0.5f, 0.5f), Position = new WorldPosition(0, 0.5f, 0), Mass = 5
        });
        scene.Run(3);
        Assert.That(scene.World.IsAwake(box), Is.False);

        scene.World.SetMass(box, 5, 300);

        Assert.That(scene.World.IsAwake(box), Is.False);
    }

    // Contacts a body has outlive a change to its weight: no point is lost.
    [Test]
    public void BodyWeighedAnew_KeepsItsContacts()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(1);
        var before = new List<ContactPoint>();
        scene.World.GetContactPoints(before);

        // Read before a step, which would find torn-off contacts again.
        scene.World.SetMass(box, 5, 0);
        var after = new List<ContactPoint>();
        scene.World.GetContactPoints(after);
        scene.World.Step();

        Assert.That(after.Count, Is.EqualTo(before.Count));
        Assert.That(before.Count, Is.GreaterThan(0));
        Assert.That(scene.World.GetPose(box).Position.Y, Is.EqualTo(0.5f).Within(0.02f));
    }
}
