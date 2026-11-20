using System;
using Adamantium.DarkMatter.Shapes;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>Joints: a pendulum, a door on its hinge, a motor, a slider, a weld, a rope, a spring and a chain, and the
/// bodies a joint holds not colliding.</summary>
[TestFixture]
public class JointTests
{
    // A ball on a rod two meters long, let go level with its pivot: the rod keeps its length, and the ball swings back
    // up nearly as high on the other side.
    [Test]
    public void Pendulum_KeepsItsLength_AndSwingsBackUp()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var pivot = new Vector3<double>(0, 5, 0);
        var ball = AddSphere(world, new Vector3F(2, 5, 0), 0.1f);
        world.Add(new JointDescription
        {
            Kind = JointKind.Distance, First = ball, Point = new Vector3<double>(2, 5, 0), SecondPoint = pivot
        });

        var longest = 0f;
        var shortest = float.MaxValue;
        var highestOnTheOtherSide = float.MinValue;
        for (var step = 0; step < 3 * 60; step++)
        {
            world.Step();
            var position = world.GetPose(ball).Position;
            var length = (float)(position - pivot).Length();
            longest = MathF.Max(longest, length);
            shortest = MathF.Min(shortest, length);
            if (position.X < 0)
                highestOnTheOtherSide = MathF.Max(highestOnTheOtherSide, (float)position.Y);
        }

        Assert.Multiple(() =>
        {
            Assert.That(longest, Is.LessThan(2.02f));
            Assert.That(shortest, Is.GreaterThan(1.98f));
            Assert.That(highestOnTheOtherSide, Is.GreaterThan(4.8f), "little lost to the swing");
        });
    }

    // A door hinged to the world along Y at its edge: pushed, it turns about the hinge, does not sag, and stops at its
    // limit of a quarter turn.
    [Test]
    public void Door_TurnsAboutItsHinge_AndStopsAtItsLimit()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var door = world.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(new Vector3F(0.5f, 1, 0.05f)), Position = new Vector3<double>(0.5f, 2, 0)
        });
        var hinge = world.Add(new JointDescription
        {
            Kind = JointKind.Hinge, First = door, Point = new Vector3<double>(0, 2, 0), Axis = Vector3F.UnitY,
            HasLimits = true, LowerLimit = -MathF.PI / 2, UpperLimit = MathF.PI / 2
        });
        // Swung about the hinge: its middle, half a meter out, moves with the turn.
        world.SetAngularVelocity(door, new Vector3F(0, -6, 0));
        world.SetLinearVelocity(door, new Vector3F(0, 0, 3));

        var widest = 0f;
        for (var step = 0; step < 120; step++)
        {
            world.Step();
            widest = MathF.Max(widest, MathF.Abs(world.GetJointPosition(hinge)));
        }

        var pose = world.GetPose(door);
        Assert.Multiple(() =>
        {
            Assert.That(widest, Is.LessThan(MathF.PI / 2 + 0.05f), "held at its limit");
            Assert.That(widest, Is.GreaterThan(MathF.PI / 2 - 0.05f), "it reached it");
            Assert.That(pose.Position.Y, Is.EqualTo(2).Within(0.01f), "no sag");
            Assert.That(Math.Sqrt(pose.Position.X * pose.Position.X + pose.Position.Z * pose.Position.Z),
                Is.EqualTo(0.5f).Within(0.01f));
        });
    }

    // A wheel of 52 kg m² driven with 100 N m: up to its 3 rad/s in a second and a half, and held there.
    [Test]
    public void HingeMotor_SpinsAWheelUpToItsSpeed()
    {
        IPhysicsWorld world = new PhysicsWorld(new PhysicsSettings { Gravity = Vector3F.Zero });
        var wheel = AddSphere(world, Vector3F.Zero, 0.5f);
        world.Add(new JointDescription
        {
            Kind = JointKind.Hinge, First = wheel, Point = Vector3<double>.Zero, Axis = Vector3F.UnitX, MotorSpeed = 3,
            MaxMotorForce = 100
        });

        for (var step = 0; step < 3 * world.Settings.StepRate; step++)
        {
            world.Step();
        }

        var spin = world.GetAngularVelocity(wheel);
        Assert.Multiple(() =>
        {
            Assert.That(spin.X, Is.EqualTo(3).Within(0.05f));
            Assert.That(MathF.Sqrt(spin.Y * spin.Y + spin.Z * spin.Z), Is.LessThan(0.01f));
        });
    }

    // A box on a rail along X: gravity does not pull it off, it slides along, and stops at the rail's end a meter on.
    [Test]
    public void Slider_KeepsToItsRail_AndStopsAtItsEnd()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var box = world.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(new Vector3F(0.2f)), Position = new Vector3<double>(0, 3, 0),
            LinearVelocity = new Vector3F(2, 0, 0)
        });
        var rail = world.Add(new JointDescription
        {
            Kind = JointKind.Slider, First = box, Point = new Vector3<double>(0, 3, 0), Axis = Vector3F.UnitX,
            HasLimits = true, LowerLimit = -1, UpperLimit = 1
        });

        for (var step = 0; step < 90; step++)
        {
            world.Step();
        }

        var pose = world.GetPose(box);
        Assert.Multiple(() =>
        {
            Assert.That(pose.Position.X, Is.EqualTo(1).Within(0.02f));
            Assert.That(world.GetJointPosition(rail), Is.EqualTo(1).Within(0.02f));
            Assert.That(pose.Position.Y, Is.EqualTo(3).Within(0.01f));
            Assert.That(MathF.Abs(QuaternionF.Dot(pose.Rotation, QuaternionF.Identity)), Is.GreaterThan(0.9999f),
                "not turned");
        });
    }

    [Test]
    public void Weld_HoldsABoxWhereItWas()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var box = world.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(new Vector3F(1, 0.1f, 0.1f)), Position = new Vector3<double>(1, 3, 0)
        });
        world.Add(new JointDescription { Kind = JointKind.Fixed, First = box, Point = new Vector3<double>(0, 3, 0) });

        for (var step = 0; step < 120; step++)
        {
            world.Step();
        }

        var pose = world.GetPose(box);
        Assert.Multiple(() =>
        {
            Assert.That((pose.Position - new Vector3<double>(1, 3, 0)).Length(), Is.LessThan(0.01f),
                "a beam held at one end");
            Assert.That(MathF.Abs(QuaternionF.Dot(pose.Rotation, QuaternionF.Identity)), Is.GreaterThan(0.9999f));
        });
    }

    // A rope two meters long: the ball falls freely until it is taut.
    [Test]
    public void Rope_LetsTheBallFall_UntilTaut()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var ball = AddSphere(world, new Vector3F(0, 4.5f, 0), 0.1f);
        world.Add(new JointDescription
        {
            Kind = JointKind.Distance, First = ball, Point = new Vector3<double>(0, 4.5f, 0), SecondPoint = new Vector3<double>(0, 5, 0),
            HasLimits = true, LowerLimit = 0, UpperLimit = 2
        });

        for (var step = 0; step < 120; step++)
        {
            world.Step();
        }

        Assert.That(world.GetPose(ball).Position.Y, Is.EqualTo(3).Within(0.02f));
    }

    // A kilogram on a spring of 2 hertz: it settles a little below the spring's rest, by m g / k.
    [Test]
    public void Spring_SettlesWhereItsPullMatchesTheWeight()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var ball = world.Add(new BodyDescription
        {
            Shape = new SphereCollisionShape(0.1f), Mass = 1, Position = new Vector3<double>(0, 4, 0)
        });
        world.Add(new JointDescription
        {
            Kind = JointKind.Distance, First = ball, Point = new Vector3<double>(0, 4, 0), SecondPoint = new Vector3<double>(0, 5, 0),
            SpringHertz = 2, SpringDampingRatio = 1
        });

        for (var step = 0; step < 5 * 60; step++)
        {
            world.Step();
        }

        var stiffness = MathF.Pow(2 * MathF.PI * 2, 2);
        var sag = 9.81f / stiffness;
        Assert.That(world.GetPose(ball).Position.Y, Is.EqualTo(4 - sag).Within(0.01f));
    }

    // Ten heavy-ended links on balls, hung from the world and nudged: the chain holds together, its links no farther
    // apart than their joints, and hangs back straight down.
    [Test]
    public void Chain_HoldsTogether_AndHangsStraight()
    {
        IPhysicsWorld world = new PhysicsWorld();
        BodyHandle previous = default;
        BodyHandle last = default;
        for (var index = 0; index < 10; index++)
        {
            var link = world.Add(new BodyDescription
            {
                Shape = new CapsuleCollisionShape(0.4f, 0.05f), Position = new Vector3<double>(0, 9.5f - index, 0),
                AngularDamping = 0.5f, LinearDamping = 0.2f
            });
            world.Add(new JointDescription
            {
                Kind = JointKind.Ball, First = link, Second = previous, Point = new Vector3<double>(0, 10 - index, 0)
            });
            previous = link;
            last = link;
        }

        world.SetLinearVelocity(last, new Vector3F(3, 0, 0));
        for (var step = 0; step < 10 * 60; step++)
        {
            world.Step();
        }

        var end = world.GetPose(last).Position;
        Assert.Multiple(() =>
        {
            Assert.That(end.Y, Is.EqualTo(0.5f).Within(0.05f), "the last link's middle, nine and a half below");
            Assert.That(Math.Abs(end.X), Is.LessThan(0.05f), "straight down again");
        });
    }

    [Test]
    public void BodiesAJointHolds_DoNotCollide_UnlessTold()
    {
        IPhysicsWorld world = new PhysicsWorld(new PhysicsSettings { Gravity = Vector3F.Zero });
        var first = world.Add(new BodyDescription { Shape = new BoxCollisionShape(new Vector3F(0.5f)) });
        var second = world.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(new Vector3F(0.5f)), Position = new Vector3<double>(0.6f, 0, 0)
        });
        var joint = world.Add(new JointDescription { Kind = JointKind.Distance, First = first, Second = second,
            SecondPoint = new Vector3<double>(0.6f, 0, 0), HasLimits = true, LowerLimit = 0, UpperLimit = 10 });

        for (var step = 0; step < 30; step++)
        {
            world.Step();
        }

        var heldTogether = world.GetPose(second).Position.X - world.GetPose(first).Position.X;
        world.Remove(joint);
        for (var step = 0; step < 60; step++)
        {
            world.Step();
        }

        var pushedApart = world.GetPose(second).Position.X - world.GetPose(first).Position.X;
        Assert.Multiple(() =>
        {
            Assert.That(heldTogether, Is.EqualTo(0.6f).Within(1e-3f), "overlapping, not pushed apart");
            Assert.That(pushedApart, Is.GreaterThan(0.95f), "free again, they collide");
        });
    }

    // A box hangs from a kinematic platform on a ball; both fall asleep. Driven off, the platform takes the box along.
    [Test]
    public void APlatformDrivenOff_WakesWhatHangsFromIt()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var platform = world.Add(new BodyDescription
        {
            Kind = BodyKind.Kinematic, Shape = new BoxCollisionShape(new Vector3F(1, 0.1f, 1)),
            Position = new Vector3<double>(0, 5, 0)
        });
        var box = world.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(new Vector3F(0.2f)), Position = new Vector3<double>(0, 4, 0)
        });
        world.Add(new JointDescription { Kind = JointKind.Ball, First = box, Second = platform, Point = new Vector3<double>(0, 4.9f, 0) });
        for (var step = 0; step < 3 * world.Settings.StepRate; step++)
        {
            world.Step();
        }

        var asleep = !world.IsAwake(box);
        world.SetLinearVelocity(platform, new Vector3F(1, 0, 0));
        for (var step = 0; step < world.Settings.StepRate; step++)
        {
            world.Step();
        }

        Assert.Multiple(() =>
        {
            Assert.That(asleep, Is.True, "both at rest first");
            Assert.That(world.GetPose(box).Position.X, Is.GreaterThan(0.8f), "carried along");
        });
    }

    // A lamp asleep on a static hook: the hook put elsewhere by hand, the lamp wakes and follows it.
    [Test]
    public void AHookPutElsewhere_WakesTheLampOnIt()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var hook = world.Add(new BodyDescription
        {
            Kind = BodyKind.Static, Shape = new SphereCollisionShape(0.05f), Position = new Vector3<double>(0, 5, 0)
        });
        var lamp = AddSphere(world, new Vector3F(0, 4, 0), 0.2f);
        world.Add(new JointDescription
        {
            Kind = JointKind.Distance, First = lamp, Second = hook, Point = new Vector3<double>(0, 4, 0),
            SecondPoint = new Vector3<double>(0, 5, 0)
        });
        for (var step = 0; step < 3 * world.Settings.StepRate; step++)
        {
            world.Step();
        }

        var asleep = !world.IsAwake(lamp);
        world.SetPose(hook, new Pose(new Vector3<double>(3, 5, 0)));
        for (var step = 0; step < 3 * world.Settings.StepRate; step++)
        {
            world.Step();
        }

        var position = world.GetPose(lamp).Position;
        Assert.Multiple(() =>
        {
            Assert.That(asleep, Is.True);
            Assert.That((position - new Vector3<double>(3, 5, 0)).Length(), Is.EqualTo(1).Within(0.02f),
                "on its rod again");
        });
    }

    [Test]
    public void AMotorOnAJointWithoutOne_AndAHingeLimitPastHalfATurn_AreRefused()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var ball = AddSphere(world, Vector3F.Zero, 0.1f);
        var joint = world.Add(new JointDescription { Kind = JointKind.Ball, First = ball });

        Assert.Throws<InvalidOperationException>(() => world.SetMotor(joint, 1, 1));
        Assert.Throws<ArgumentException>(() => world.Add(new JointDescription
        {
            Kind = JointKind.Hinge, First = ball, HasLimits = true, LowerLimit = 0, UpperLimit = 3.5f
        }));
    }

    [Test]
    public void ABodyTakenOut_TakesItsJoints_AndBadJointsAreRefused()
    {
        IPhysicsWorld world = new PhysicsWorld();
        var ball = AddSphere(world, Vector3F.Zero, 0.1f);
        var other = AddSphere(world, Vector3F.UnitX, 0.1f);
        var joint = world.Add(new JointDescription { First = ball, Second = other });

        Assert.Throws<ArgumentException>(() => world.Add(new JointDescription { First = ball, Second = ball }));
        Assert.Throws<ArgumentException>(() => world.Add(new JointDescription
        {
            Kind = JointKind.Hinge, First = ball, Axis = Vector3F.Zero
        }));
        Assert.Throws<ArgumentException>(() => world.Add(new JointDescription
        {
            First = ball, HasLimits = true, LowerLimit = 1, UpperLimit = 0
        }));
        Assert.Throws<ArgumentException>(() => world.Add(new JointDescription
        {
            Kind = JointKind.Distance, First = ball, Point = Vector3<double>.Zero, SecondPoint = Vector3<double>.Zero
        }));

        world.Remove(other);
        Assert.Multiple(() =>
        {
            Assert.That(world.Contains(joint), Is.False);
            Assert.That(world.JointCount, Is.Zero);
            Assert.Throws<ArgumentException>(() => world.Remove(joint));
        });
    }

    private static BodyHandle AddSphere(IPhysicsWorld world, Vector3F position, float radius) =>
        world.Add(new BodyDescription { Shape = new SphereCollisionShape(radius), Position = position.As<double>() });
}
