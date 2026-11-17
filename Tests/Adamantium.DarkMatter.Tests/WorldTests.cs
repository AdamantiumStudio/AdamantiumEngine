using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

[TestFixture]
public class WorldTests
{
    [TestCase(1)]
    [TestCase(4)]
    [TestCase(8)]
    public void ContactPoints_StayWithinTheLimitPerPair(int limit)
    {
        var scene = new SceneBuilder(new PhysicsSettings { MaxContactPointsPerPair = limit, Gravity = Vector3F.Zero });
        scene.AddBox(Vector3F.Zero, new Vector3F(0.5f));
        var turned = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, MathF.PI / 4);
        scene.AddBox(new Vector3F(0, 0.99f, 0), new Vector3F(0.5f), rotation: turned);

        scene.World.Step();

        var points = new List<ContactPoint>();
        scene.World.GetContactPoints(points);
        Assert.That(points, Has.Count.EqualTo(limit));
    }

    [Test]
    public void RemovedBody_LeavesTheWorldAndItsHandleGoesStale()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(0.5f);

        scene.World.Remove(box);
        var other = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(0.5f);

        Assert.That(scene.World.Contains(box), Is.False);
        Assert.That(scene.World.Contains(other), Is.True);
        Assert.That(other, Is.Not.EqualTo(box));
        Assert.That(scene.World.BodyCount, Is.EqualTo(2));
        Assert.Throws<ArgumentException>(() => scene.World.GetPose(box));
    }

    [Test]
    public void HandleOfAnotherWorld_IsRefused()
    {
        IPhysicsWorld host = new PhysicsWorld();
        IPhysicsWorld client = new PhysicsWorld();
        var hostBody = host.Add(new RigidBodyDescription { Shape = new Shapes.SphereShape(0.5f) });
        var clientBody = client.Add(new RigidBodyDescription { Shape = new Shapes.SphereShape(0.5f) });

        Assert.That(hostBody, Is.Not.EqualTo(clientBody));
        Assert.That(client.Contains(hostBody), Is.False);
        Assert.Throws<ArgumentException>(() => client.Remove(hostBody));
        Assert.That(client.BodyCount, Is.EqualTo(1));
    }

    [Test]
    public void RemovingTheGround_WakesWhatSleptOnIt()
    {
        var scene = new SceneBuilder();
        var ground = scene.AddGround();
        var box = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(2f);
        Assert.That(scene.World.IsAwake(box), Is.False);

        scene.World.Remove(ground);
        scene.Run(0.5f);

        Assert.That(scene.World.GetPose(box).Position.Y, Is.LessThan(0));
    }

    [Test]
    public void KinematicPlatform_CarriesABoxAndIsNotPushedBack()
    {
        var scene = new SceneBuilder();
        var platform = scene.World.Add(new RigidBodyDescription
        {
            Kind = BodyKind.Kinematic,
            Shape = new Shapes.BoxShape(2, 0.25f, 2),
            LinearVelocity = new Vector3F(0, 0.5f, 0)
        });
        var box = scene.AddBox(new Vector3F(0, 0.75f, 0), new Vector3F(0.5f));

        scene.Run(2f);

        Assert.That(scene.World.GetPose(platform).Position.Y, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(scene.World.GetPose(box).Position.Y, Is.EqualTo(1.75f).Within(0.02f));
    }

    [Test]
    public void StaticBodyPlacedIntoASleepingBox_WakesIt()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(2f);
        Assert.That(scene.World.IsAwake(box), Is.False);

        scene.World.Add(new RigidBodyDescription
        {
            Kind = BodyKind.Static,
            Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
            Position = new Vector3F(0.8f, 0.5f, 0)
        });
        scene.World.Step();

        Assert.That(scene.World.IsAwake(box), Is.True);
    }

    [Test]
    public void SleepingBodies_HoldNoVelocity()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var boxes = new List<RigidBody>();
        for (var level = 0; level < 4; level++)
        {
            boxes.Add(scene.World.Add(new RigidBodyDescription
            {
                Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
                Position = new Vector3F(0, 0.5f + level, 0),
                IsAwake = false
            }));
        }

        scene.World.Step();
        scene.World.ApplyImpulse(boxes[0], new Vector3F(300, 0, 0), new Vector3F(0, 0.5f, 0));
        for (var step = 0; step < 240; step++)
        {
            scene.World.Step();
            foreach (var box in boxes)
            {
                if (scene.World.IsAwake(box))
                    continue;

                Assert.That(scene.World.GetLinearVelocity(box), Is.EqualTo(Vector3F.Zero), $"step {step}");
                Assert.That(scene.World.GetAngularVelocity(box), Is.EqualTo(Vector3F.Zero), $"step {step}");
            }
        }
    }

    [Test]
    public void RemovingEveryBody_LeavesNoPairs()
    {
        var scene = new SceneBuilder();
        var bodies = new List<RigidBody> { scene.AddGround() };
        for (var level = 0; level < 5; level++)
        {
            bodies.Add(scene.AddBox(new Vector3F(0, 0.5f + level, 0), new Vector3F(0.5f)));
        }

        scene.Run(1f);
        bodies.Reverse();
        foreach (var body in bodies)
        {
            scene.World.Remove(body);
            scene.World.Step();
        }

        Assert.That(scene.World.BodyCount, Is.EqualTo(0));
        Assert.That(scene.World.LastStep.Pairs, Is.EqualTo(0));
    }

    [Test]
    public void SlidingBox_StopsWhereFrictionSays()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f), velocity: new Vector3F(5, 0, 0));

        scene.Run(3f);

        var expected = 5f * 5f / (2f * 0.6f * 9.81f);
        Assert.That(scene.World.GetPose(box).Position.X, Is.EqualTo(expected).Within(0.1f * expected));
    }

    [Test]
    public void MassWithoutDensity_MakesAHeavyBody()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.World.Add(new RigidBodyDescription
        {
            Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
            Position = new Vector3F(0, 0.5f, 0),
            Density = 0,
            Mass = 5
        });

        scene.Run(1f);

        Assert.That(scene.World.GetPose(box).Position.Y, Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void BadDescriptions_AreRefused()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var cube = new Shapes.BoxShape(0.5f, 0.5f, 0.5f);

        Assert.Throws<ArgumentException>(() => world.Add(new RigidBodyDescription { Shape = cube, Density = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Add(new RigidBodyDescription { Shape = cube, Mass = -1 }));
        Assert.Throws<ArgumentException>(() => world.Add(new RigidBodyDescription { Shape = cube, Rotation = default }));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Add(new RigidBodyDescription { Shape = cube, Friction = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            world.Add(new RigidBodyDescription { Shape = cube, Position = new Vector3F(float.NaN, 0, 0) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsWorld(new PhysicsSettings { ContactHertz = float.NaN }));
        var body = world.Add(new RigidBodyDescription { Shape = cube });
        Assert.Throws<ArgumentException>(() => world.SetPose(body, new Pose { Position = Vector3F.One }));
        Assert.That(world.GetPose(body).Position, Is.EqualTo(Vector3F.Zero), "a refused pose leaves the body where it was");
        Assert.That(world.BodyCount, Is.EqualTo(1));
    }

    [Test]
    public void BodiesAddedAsleepOnTheGround_StayAsleep()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.World.Add(new RigidBodyDescription
        {
            Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
            Position = new Vector3F(0, 0.5f, 0),
            IsAwake = false
        });

        scene.Run(0.5f);

        Assert.That(scene.World.IsAwake(box), Is.False);
    }

    [Test]
    public void SleeperAddedOntoASleepingStack_WakesNothing()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var bottom = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(2f);
        Assert.That(scene.World.IsAwake(bottom), Is.False);

        var top = scene.World.Add(new RigidBodyDescription
        {
            Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
            Position = new Vector3F(0, 1.5f, 0),
            LinearVelocity = new Vector3F(0, 50, 0),
            IsAwake = false
        });
        scene.Run(0.5f);

        Assert.That(scene.World.IsAwake(bottom), Is.False);
        Assert.That(scene.World.IsAwake(top), Is.False);
        Assert.That(scene.World.GetLinearVelocity(top), Is.EqualTo(Vector3F.Zero), "a body added asleep holds no velocity");
    }

    [Test]
    public void RemovingWhatASleeperAddedAsleepRestsOn_DropsIt()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var bottom = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(2f);
        var top = scene.World.Add(new RigidBodyDescription
        {
            Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
            Position = new Vector3F(0, 1.5f, 0),
            IsAwake = false
        });
        scene.World.Step();

        scene.World.Remove(bottom);
        scene.Run(1f);

        Assert.That(scene.World.GetPose(top).Position.Y, Is.EqualTo(0.5f).Within(0.02f));
    }

    [Test]
    public void KinematicAddedAsleepWithVelocity_Moves()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var platform = world.Add(new RigidBodyDescription
        {
            Kind = BodyKind.Kinematic,
            Shape = new Shapes.BoxShape(1, 0.1f, 1),
            LinearVelocity = Vector3F.UnitX,
            IsAwake = false
        });

        for (var step = 0; step < 120; step++)
        {
            world.Step();
        }

        Assert.That(world.GetPose(platform).Position.X, Is.EqualTo(1f).Within(1e-3f));
    }

    [Test]
    public void StackLoadedAsleepOnAStillPlatform_StaysAsleep()
    {
        var scene = new SceneBuilder();
        scene.World.Add(new RigidBodyDescription
        {
            Kind = BodyKind.Kinematic,
            Shape = new Shapes.BoxShape(2, 0.25f, 2)
        });
        var boxes = new List<RigidBody>();
        for (var level = 0; level < 3; level++)
        {
            boxes.Add(scene.World.Add(new RigidBodyDescription
            {
                Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
                Position = new Vector3F(0, 0.75f + level, 0),
                IsAwake = false
            }));
        }

        scene.Run(0.5f);

        foreach (var box in boxes)
        {
            Assert.That(scene.World.IsAwake(box), Is.False);
        }
    }

    [Test]
    public void StillPlatformGivenZeroVelocity_LeavesTheStackAsleep()
    {
        var scene = new SceneBuilder();
        var platform = scene.World.Add(new RigidBodyDescription
        {
            Kind = BodyKind.Kinematic,
            Shape = new Shapes.BoxShape(2, 0.25f, 2)
        });
        var box = scene.AddBox(new Vector3F(0, 0.75f, 0), new Vector3F(0.5f));
        scene.Run(2f);
        Assert.That(scene.World.IsAwake(box), Is.False);

        for (var step = 0; step < 120; step++)
        {
            scene.World.SetLinearVelocity(platform, Vector3F.Zero);
            scene.World.Step();
            Assert.That(scene.World.IsAwake(box), Is.False, $"step {step}");
        }
    }

    [Test]
    public void SupportRemovedBeforeTheFirstStep_DropsTheSleeperOnIt()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var support = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(2f);
        var top = scene.World.Add(new RigidBodyDescription
        {
            Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
            Position = new Vector3F(0, 1.5f, 0),
            IsAwake = false
        });

        scene.World.Remove(support);
        scene.Run(1f);

        Assert.That(scene.World.GetPose(top).Position.Y, Is.EqualTo(0.5f).Within(0.02f));
    }

    [Test]
    public void HugeVelocity_IsCutToTheSpeedLimit()
    {
        IPhysicsWorld world = new PhysicsWorld(new PhysicsSettings { Gravity = Vector3F.Zero });
        var ball = world.Add(new RigidBodyDescription { Shape = new Shapes.SphereShape(0.5f) });
        world.SetLinearVelocity(ball, new Vector3F(3e38f, 0, 0));
        Assert.That(world.GetLinearVelocity(ball).Length(), Is.EqualTo(500f).Within(0.01f));

        world.Step();

        Assert.That(world.GetLinearVelocity(ball).Length(), Is.EqualTo(500f).Within(0.01f));
        Assert.That(world.GetPose(ball).Position.X, Is.EqualTo(500f / 120f).Within(1e-3f));
    }

    [Test]
    public void ExtremeMotion_StaysFinite()
    {
        IPhysicsWorld world = new PhysicsWorld();
        world.Add(new RigidBodyDescription
        {
            Kind = BodyKind.Static,
            Shape = new Shapes.BoxShape(50, 1, 50),
            Position = new Vector3F(0, -1, 0)
        });
        var spinner = world.Add(new RigidBodyDescription { Shape = new Shapes.BoxShape(1, 0.1f, 0.1f), Position = Vector3F.UnitY });
        var heavy = world.Add(new RigidBodyDescription
        {
            Shape = new Shapes.SphereShape(0.5f),
            Position = new Vector3F(3, 1.5f, 0),
            GravityScale = 1e38f
        });
        var underHeavy = world.Add(new RigidBodyDescription
        {
            Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
            Position = new Vector3F(3, 0.5f, 0)
        });
        world.SetAngularVelocity(spinner, new Vector3F(1e20f, 0, 0));
        Assert.That(world.GetAngularVelocity(spinner).Length(), Is.EqualTo(world.MaxAngularSpeed).Within(0.01f));

        for (var step = 0; step < 60; step++)
        {
            world.Step();
        }

        var points = new List<ContactPoint>();
        world.GetContactPoints(points);
        foreach (var point in points)
        {
            Assert.That(float.IsFinite(point.NormalImpulse), Is.True, "no impulse turns into NaN");
        }

        RigidBody[] bodies = [spinner, heavy, underHeavy];
        foreach (var body in bodies)
        {
            var pose = world.GetPose(body);
            Assert.That(float.IsFinite(pose.Position.X + pose.Position.Y + pose.Position.Z), Is.True);
            Assert.That(world.GetLinearVelocity(body).Length(), Is.LessThanOrEqualTo(500.01f));
        }
    }

    [TestCase(1e12f, 1f)]
    [TestCase(1e8f, 1e20f)]
    [TestCase(1e-6f, 1f)]
    public void GivenMass_WorksForHugeAndTinyShapes(float radius, float mass)
    {
        IPhysicsWorld world = new PhysicsWorld();
        var body = world.Add(new RigidBodyDescription { Shape = new Shapes.SphereShape(radius), Mass = mass });

        world.ApplyImpulse(body, new Vector3F(mass, 0, 0), Vector3F.Zero);

        Assert.That(world.GetLinearVelocity(body).X, Is.EqualTo(1f).Within(1e-3f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsWorld(new PhysicsSettings { MaxLinearSpeed = float.MaxValue }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicsWorld(new PhysicsSettings { MaxLinearSpeed = -500 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Shapes.SphereShape(1e-7f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Shapes.SphereShape(5e12f));
    }

    [Test]
    public void PlanetSizedStaticGround_IsAccepted()
    {
        IPhysicsWorld world = new PhysicsWorld();
        world.Add(new RigidBodyDescription
        {
            Kind = BodyKind.Static,
            Shape = new Shapes.BoxShape(5e10f, 1, 5e10f),
            Position = new Vector3F(0, -1, 0)
        });

        Assert.That(world.BodyCount, Is.EqualTo(1));
    }

    [Test]
    public void SlotReusedBySleeper_WakesNothing()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var left = scene.AddBox(new Vector3F(-0.5f, 0.5f, 0), new Vector3F(0.5f));
        var far = scene.AddBox(new Vector3F(5, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(2f);
        Assert.That(scene.World.IsAwake(left), Is.False);

        scene.World.Remove(far);
        var replacement = scene.World.Add(new RigidBodyDescription
        {
            Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
            Position = new Vector3F(0.5f, 0.5f, 0),
            IsAwake = false
        });
        scene.World.Step();

        Assert.That(scene.World.IsAwake(replacement), Is.False);
        Assert.That(scene.World.IsAwake(left), Is.False);
    }

    [Test]
    public void ChangingGravity_LeavesBodiesItDoesNotPullAsleep()
    {
        var scene = new SceneBuilder(new PhysicsSettings { Gravity = Vector3F.Zero });
        var floating = scene.World.Add(new RigidBodyDescription
        {
            Shape = new Shapes.SphereShape(0.5f),
            GravityScale = 0
        });
        scene.Run(1f);
        Assert.That(scene.World.IsAwake(floating), Is.False);

        scene.World.Gravity = new Vector3F(0, -9.81f, 0);

        Assert.That(scene.World.IsAwake(floating), Is.False);
    }

    [Test]
    public void BadMotion_IsRefused()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var body = world.Add(new RigidBodyDescription { Shape = new Shapes.SphereShape(0.5f) });
        var bad = new Vector3F(float.NaN, 0, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => world.SetLinearVelocity(body, bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.SetAngularVelocity(body, bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.ApplyForce(body, bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.ApplyTorque(body, bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.ApplyImpulse(body, bad, Vector3F.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Shapes.BoxShape(float.NaN, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Shapes.BoxShape(3e38f, 3e38f, 3e38f));
        Assert.Throws<ArgumentException>(() =>
            world.Add(new RigidBodyDescription { Shape = new Shapes.SphereShape(0.01f), Density = 1e-40f }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            world.Add(new RigidBodyDescription { Shape = new Shapes.SphereShape(1), Kind = (BodyKind)7 }));
        Assert.Throws<ArgumentException>(() =>
            world.Add(new RigidBodyDescription { Shape = new Shapes.SphereShape(1), Mass = 1e-39f }));

        world.ApplyForce(body, new Vector3F(3e38f, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.ApplyForce(body, new Vector3F(3e38f, 0, 0)));
    }

    [Test]
    public void GroundLiftedIntoAHoveringSleeper_WakesIt()
    {
        var scene = new SceneBuilder();
        var ground = scene.AddGround();
        var box = scene.World.Add(new RigidBodyDescription
        {
            Shape = new Shapes.BoxShape(0.5f, 0.5f, 0.5f),
            Position = new Vector3F(0, 0.6f, 0),
            IsAwake = false
        });
        scene.Run(0.1f);
        Assert.That(scene.World.IsAwake(box), Is.False);

        scene.World.SetPose(ground, new Pose(new Vector3F(0, -0.7f, 0)));
        scene.World.Step();

        Assert.That(scene.World.IsAwake(box), Is.True);
    }

    [Test]
    public void ChangingGravity_WakesSleepers()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var box = scene.AddBox(new Vector3F(0, 0.5f, 0), new Vector3F(0.5f));
        scene.Run(2f);
        Assert.That(scene.World.IsAwake(box), Is.False);

        scene.World.Gravity = new Vector3F(0, 9.81f, 0);
        scene.Run(0.5f);

        Assert.That(scene.World.GetPose(box).Position.Y, Is.GreaterThan(1f));
    }

    [Test]
    public void SpinningBar_DoesNotSwingThroughAThinWall()
    {
        var scene = new SceneBuilder(new PhysicsSettings { Gravity = Vector3F.Zero });
        scene.World.Add(new RigidBodyDescription
        {
            Kind = BodyKind.Static,
            Shape = new Shapes.BoxShape(0.025f, 3, 3),
            Position = new Vector3F(1.5f, 0, 0)
        });
        var bar = scene.World.Add(new RigidBodyDescription
        {
            Shape = new Shapes.BoxShape(2, 0.05f, 0.05f),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitY, MathF.PI / 2),
            AngularVelocity = new Vector3F(0, 25f, 0)
        });

        for (var step = 0; step < 120; step++)
        {
            scene.World.Step();
            var bounds = scene.World.GetBounds(bar);
            Assert.That(bounds.Maximum.X, Is.LessThan(1.5f), $"step {step}: the bar reached past the wall");
        }
    }

    [Test]
    public void StaticBody_RefusesVelocity()
    {
        var scene = new SceneBuilder();
        var ground = scene.AddGround();

        Assert.Throws<InvalidOperationException>(() => scene.World.SetLinearVelocity(ground, Vector3F.UnitX));
        Assert.Throws<InvalidOperationException>(() => scene.World.ApplyForce(ground, Vector3F.UnitX));
    }
}
