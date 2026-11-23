using System;
using System.Collections.Generic;
using System.Diagnostics;
using Adamantium.DarkMatter.Shapes;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.DarkMatter.Tests;

/// <summary>How two materials meet, and the material of a body's parts: their own, or the body's; how a body holds and
/// then slides, how it is held back rolling, and the surface it is told by.</summary>
[TestFixture]
public class PhysicsMaterialTests
{
    private const float Gravity = 9.81f;
    private static readonly BoxCollisionShape Crate = new(0.5f, 0.5f, 0.5f);
    private static readonly BoxCollisionShape Half = new(5, 0.5f, 5);
    private static readonly PhysicsMaterial Ice = new()
    {
        StaticFriction = 0, DynamicFriction = 0, FrictionCombine = CombineRule.Minimum, Surface = "Ice"
    };

    private static readonly PhysicsMaterial Rubber = new()
    {
        StaticFriction = 1, DynamicFriction = 1, FrictionCombine = CombineRule.Maximum
    };

    [TestCase(CombineRule.Average, CombineRule.Average, 0.5f)]
    [TestCase(CombineRule.Minimum, CombineRule.Average, 0.2f)]
    [TestCase(CombineRule.Multiply, CombineRule.Minimum, 0.16f)]
    [TestCase(CombineRule.Maximum, CombineRule.Multiply, 0.8f)]
    [TestCase(CombineRule.Average, CombineRule.Maximum, 0.8f)]
    public void TwoMaterials_MeetByTheLaterOfTheirRules(CombineRule first, CombineRule second, float expected)
    {
        var low = new PhysicsMaterial
        {
            StaticFriction = 0.2f, DynamicFriction = 0.2f, RollingResistance = 0.2f, Restitution = 0.2f,
            FrictionCombine = first, RestitutionCombine = first
        };
        var high = new PhysicsMaterial
        {
            StaticFriction = 0.8f, DynamicFriction = 0.8f, RollingResistance = 0.8f, Restitution = 0.8f,
            FrictionCombine = second, RestitutionCombine = second
        };

        Assert.Multiple(() =>
        {
            Assert.That(PhysicsMaterial.CombineStaticFriction(low, high), Is.EqualTo(expected).Within(1e-6f));
            Assert.That(PhysicsMaterial.CombineDynamicFriction(high, low), Is.EqualTo(expected).Within(1e-6f));
            Assert.That(PhysicsMaterial.CombineRollingResistance(low, high), Is.EqualTo(expected).Within(1e-6f));
            Assert.That(PhysicsMaterial.CombineRestitution(high, low), Is.EqualTo(expected).Within(1e-6f));
        });
    }

    // Two crates slid over a floor of two halves: the half of ice keeps its own material, the other has the floor's.
    [Test]
    public void APartsOwnMaterial_IsWhatItsContactsMeetBy()
    {
        using var simulation = new PhysicsSimulation();
        simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static,
            Parts =
            [
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(-5, -0.5f, 0)), Material = Ice },
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(5, -0.5f, 0)) }
            ]
        });
        var onIce = AddSlidingCrate(simulation, -5);
        var onFloor = AddSlidingCrate(simulation, 5);

        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.LinearVelocityOf(onIce).X, Is.EqualTo(2).Within(0.05f), "nothing rubs on ice");
            Assert.That(simulation.LinearVelocityOf(onFloor).X, Is.LessThan(0.05f), "stopped by the floor's");
        });
    }

    // The floor turned to ice under crates sliding on it: its half with no material of its own goes with it at once,
    // the half of rubber stays rubber.
    [Test]
    public void TheBodysMaterialSet_ReachesItsPartsWithNoneOfTheirOwn()
    {
        using var simulation = new PhysicsSimulation();
        var floor = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static,
            Parts =
            [
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(-5, -0.5f, 0)), Material = Rubber },
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(5, -0.5f, 0)) }
            ]
        });
        var onRubber = AddSlidingCrate(simulation, -5, speed: 4);
        var onFloor = AddSlidingCrate(simulation, 5, speed: 4);
        StepFor(simulation, 0.25f);
        var sliding = simulation.LinearVelocityOf(onFloor).X;

        simulation.SetMaterial(floor, Ice);
        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(sliding, Is.InRange(0.5f, 3.5f), "slowed by the floor's own, still sliding");
            Assert.That(simulation.LinearVelocityOf(onRubber).X, Is.LessThan(0.05f), "its own kept");
            Assert.That(simulation.LinearVelocityOf(onFloor).X, Is.EqualTo(sliding).Within(0.05f), "the floor's taken");
        });
    }

    // A floor of one shape given parts, one of ice: its contacts meet by the parts' materials from then on.
    [Test]
    public void PartsGivenWithAMaterial_AreMetByIt()
    {
        using var simulation = new PhysicsSimulation();
        var floor = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = new BoxCollisionShape(10, 0.5f, 5), Position = new WorldPosition(0, -0.5, 0)
        });
        simulation.SetParts(floor,
        [
            new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(-5, 0, 0)), Material = Ice },
            new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(5, 0, 0)) }
        ]);
        var onIce = AddSlidingCrate(simulation, -5);
        var onFloor = AddSlidingCrate(simulation, 5);

        StepFor(simulation, 1);

        Assert.Multiple(() =>
        {
            Assert.That(simulation.LinearVelocityOf(onIce).X, Is.EqualTo(2).Within(0.05f));
            Assert.That(simulation.LinearVelocityOf(onFloor).X, Is.LessThan(0.05f));
        });
    }

    [Test]
    public void AMaterialItCannotUse_IsRefusedWhenAsked()
    {
        using var simulation = new PhysicsSimulation();
        var crate = simulation.AddBody(new BodySettings { Shape = Crate });

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                simulation.SetMaterial(crate, new PhysicsMaterial { FrictionCombine = (CombineRule)9 }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                simulation.SetMaterial(crate, new PhysicsMaterial { RollingResistance = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => simulation.SetParts(crate,
                [new BodyPart { Shape = Crate, Material = new PhysicsMaterial { Restitution = -1 } }]));
        });
    }

    // On a slope of 20 degrees - steeper than its dynamic friction holds, gentler than its static - a crate at rest
    // stays; pushed, it slides on, faster and faster, by the dynamic.
    [Test]
    public void ACrateOnASlope_HoldsByItsStaticFriction_AndPushedSlidesByItsDynamic()
    {
        var angle = 20f * MathF.PI / 180f;
        var slope = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, angle);
        var downhill = Vector3F.Transform(-Vector3F.UnitX, slope);
        var material = new PhysicsMaterial { StaticFriction = 0.6f, DynamicFriction = 0.2f };
        var scene = new SceneBuilder();
        var ground = scene.AddGround(rotation: slope);
        scene.World.SetMaterial(ground, material);
        var start = Vector3F.Transform(new Vector3F(0, 0.5f, 0), slope);
        var crate = scene.World.Add(new BodyDescription
        {
            Shape = Crate, Position = new WorldPosition(start), Rotation = slope, Material = material
        });
        scene.Run(2f);
        var held = (scene.World.GetPose(crate).Position - new WorldPosition(start)).Length();

        scene.World.SetLinearVelocity(crate, downhill * 0.5f);
        scene.World.WakeUp(crate);
        scene.Run(1f);

        // Sliding, it gains g (sin - dynamic friction cos) each second.
        var expected = 0.5f + Gravity * (MathF.Sin(angle) - 0.2f * MathF.Cos(angle));
        Assert.Multiple(() =>
        {
            Assert.That(held, Is.LessThan(0.02f), "held by its static friction");
            Assert.That(Vector3F.Dot(scene.World.GetLinearVelocity(crate), downhill),
                Is.EqualTo(expected).Within(0.1f * expected), "sliding by its dynamic friction");
        });
    }

    // On a slope just gentler than its static friction holds - its load shifted to its lower edge - a crate stays: the
    // touch holds whole, its lower points not slipping as its upper ones would alone.
    [Test]
    public void ACrateOnASlopeNearItsStaticFriction_HoldsAsAWhole()
    {
        var slope = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, 29f * MathF.PI / 180f);
        var material = new PhysicsMaterial { StaticFriction = 0.6f, DynamicFriction = 0.3f };
        var scene = new SceneBuilder();
        scene.World.SetMaterial(scene.AddGround(rotation: slope), material);
        var start = Vector3F.Transform(new Vector3F(0, 0.5f, 0), slope);
        var crate = scene.World.Add(new BodyDescription
        {
            Shape = Crate, Position = new WorldPosition(start), Rotation = slope, Material = material
        });

        scene.Run(2f);

        Assert.That((scene.World.GetPose(crate).Position - new WorldPosition(start)).Length(), Is.LessThan(0.02f));
    }

    // A wheel standing on its rim, leaning, falls over as fast with a rolling resistance as without: falling is no
    // rolling about its axle.
    [Test]
    public void ALeaningWheel_FallsOverAsIfItHadNoRollingResistance()
    {
        var leans = new float[2];
        for (var run = 0; run < 2; run++)
        {
            var scene = new SceneBuilder();
            scene.AddGround();
            var turn = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, 0.4f)
                       * Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2);
            var wheel = scene.World.Add(new BodyDescription
            {
                Shape = new CylinderCollisionShape(0.1f, 0.4f),
                Position = new WorldPosition(Vector3F.Transform(new Vector3F(0, 0, -0.4f), turn)),
                Rotation = turn,
                Material = new PhysicsMaterial
                {
                    StaticFriction = 1, DynamicFriction = 1, RollingResistance = run == 0 ? 0 : 0.3f,
                    FrictionCombine = CombineRule.Maximum
                }
            });

            scene.Run(0.8f);
            var axle = Vector3F.Transform(Vector3F.UnitY, scene.World.GetPose(wheel).Rotation);
            leans[run] = MathF.Abs(axle.Y);
        }

        Assert.Multiple(() =>
        {
            Assert.That(leans[0], Is.GreaterThan(0.6f), "it falls over");
            Assert.That(leans[1], Is.EqualTo(leans[0]).Within(0.03f));
        });
    }

    // A dynamic friction above the static holds no harder: on a slope its static friction does not hold, the crate
    // slides by the static.
    [Test]
    public void ADynamicFrictionAboveTheStatic_SlidesByTheStatic()
    {
        var angle = 20f * MathF.PI / 180f;
        var slope = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, angle);
        var material = new PhysicsMaterial { StaticFriction = 0.2f, DynamicFriction = 0.8f };
        var scene = new SceneBuilder();
        scene.World.SetMaterial(scene.AddGround(rotation: slope), material);
        var crate = scene.World.Add(new BodyDescription
        {
            Shape = Crate, Position = new WorldPosition(Vector3F.Transform(new Vector3F(0, 0.5f, 0), slope)),
            Rotation = slope, Material = material
        });

        scene.Run(1f);

        var expected = Gravity * (MathF.Sin(angle) - 0.2f * MathF.Cos(angle));
        Assert.That(scene.World.GetLinearVelocity(crate).Length(), Is.EqualTo(expected).Within(0.15f * expected));
    }

    // A crate slid over the floor stops where its dynamic friction says, however hard its static would have held it.
    [Test]
    public void ASlidingCrate_StopsWhereItsDynamicFrictionSays()
    {
        var material = new PhysicsMaterial { StaticFriction = 0.9f, DynamicFriction = 0.3f };
        var scene = new SceneBuilder();
        scene.World.SetMaterial(scene.AddGround(), material);
        var crate = scene.World.Add(new BodyDescription
        {
            Shape = Crate, Position = new WorldPosition(0, 0.5f, 0), LinearVelocity = new Vector3F(5, 0, 0),
            Material = material
        });

        scene.Run(3f);

        var expected = 5f * 5f / (2f * 0.3f * Gravity);
        Assert.That(scene.World.GetPose(crate).Position.X, Is.EqualTo(expected).Within(0.1f * expected));
    }

    // A ball rolling at 3 m/s rolls on with none; with rolling resistance 0.05 it loses Crr g / (1 + 2/5) each second,
    // the share its own turn takes of it.
    [TestCase(0f, 3f)]
    [TestCase(0.05f, 3f - 2f * 0.05f * Gravity / 1.4f)]
    public void ARollingBall_IsHeldBackByItsRollingResistance(float resistance, float expected)
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var ball = scene.World.Add(new BodyDescription
        {
            Shape = new SphereCollisionShape(0.5f),
            Position = new WorldPosition(0, 0.5f, 0),
            LinearVelocity = new Vector3F(3, 0, 0),
            AngularVelocity = new Vector3F(0, 0, -3 / 0.5f),
            Material = new PhysicsMaterial { RollingResistance = resistance, FrictionCombine = CombineRule.Maximum }
        });

        scene.Run(2f);

        var velocity = scene.World.GetLinearVelocity(ball);
        Assert.Multiple(() =>
        {
            Assert.That(velocity.X, Is.EqualTo(expected).Within(0.1f));
            Assert.That(-scene.World.GetAngularVelocity(ball).Z * 0.5f, Is.EqualTo(velocity.X).Within(0.05f),
                "rolling, not sliding");
        });
    }

    // A tall crate let go leaning past its edge tips over as fast with a rolling resistance as without: flat, it does
    // not roll.
    [Test]
    public void ATippingCrate_IsNotHeldBackByRollingResistance()
    {
        var tilts = new float[2];
        for (var run = 0; run < 2; run++)
        {
            var scene = new SceneBuilder();
            scene.AddGround();
            var lean = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, 0.4f);
            var crate = scene.World.Add(new BodyDescription
            {
                Shape = new BoxCollisionShape(0.25f, 1, 0.25f),
                Position = new WorldPosition(Vector3F.Transform(new Vector3F(0.25f, 1, 0), lean) - new Vector3F(0.25f, 0, 0)),
                Rotation = lean,
                Material = new PhysicsMaterial
                {
                    StaticFriction = 1, DynamicFriction = 1, RollingResistance = run == 0 ? 0 : 0.3f,
                    FrictionCombine = CombineRule.Maximum
                }
            });

            scene.Run(0.8f);
            var up = Vector3F.Transform(Vector3F.UnitY, scene.World.GetPose(crate).Rotation);
            tilts[run] = MathF.Acos(Math.Clamp(up.Y, -1, 1));
        }

        Assert.Multiple(() =>
        {
            Assert.That(tilts[0], Is.GreaterThan(0.8f), "it tips");
            Assert.That(tilts[1], Is.EqualTo(tilts[0]).Within(0.02f));
        });
    }

    // A wheel rolling on its side is held back as a ball is, by its own radius.
    [Test]
    public void ARollingWheel_IsHeldBackByItsRollingResistance()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        var wheel = scene.World.Add(new BodyDescription
        {
            Shape = new CylinderCollisionShape(0.15f, 0.4f),
            Position = new WorldPosition(0, 0.4f, 0),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3F.UnitX, MathF.PI / 2),
            LinearVelocity = new Vector3F(3, 0, 0),
            AngularVelocity = new Vector3F(0, 0, -3 / 0.4f),
            Material = new PhysicsMaterial
            {
                StaticFriction = 1, DynamicFriction = 1, RollingResistance = 0.05f, FrictionCombine = CombineRule.Maximum
            }
        });

        scene.Run(1f);

        // A solid cylinder's own turn takes half again its mass: it loses Crr g / 1.5 each second.
        var expected = 3f - 0.05f * Gravity / 1.5f;
        Assert.That(scene.World.GetLinearVelocity(wheel).X, Is.EqualTo(expected).Within(0.08f));
    }

    // A ball rolling over a heavy crate - both moving bodies - rolls on its own radius, not one between it and the
    // crate's.
    [Test]
    public void ABallRollingOverAMovingCrate_RollsOnItsOwnRadius()
    {
        var scene = new SceneBuilder();
        scene.AddGround();
        scene.World.Add(new BodyDescription
        {
            Shape = new BoxCollisionShape(10, 2, 10), Position = new WorldPosition(0, 2, 0), Mass = 100000, Density = 0,
            CanSleep = false
        });
        var ball = scene.World.Add(new BodyDescription
        {
            Shape = new SphereCollisionShape(0.1f),
            Position = new WorldPosition(0, 4.1f, 0),
            LinearVelocity = new Vector3F(3, 0, 0),
            AngularVelocity = new Vector3F(0, 0, -3 / 0.1f),
            Material = new PhysicsMaterial { RollingResistance = 0.05f, FrictionCombine = CombineRule.Maximum }
        });

        scene.Run(1f);

        Assert.That(scene.World.GetLinearVelocity(ball).X, Is.EqualTo(3f - 0.05f * Gravity / 1.4f).Within(0.08f));
    }

    // On a slope of 5 degrees a ball whose rolling resistance holds more than the slope pulls stays where it was put;
    // with none it rolls away.
    [TestCase(0.3f, true)]
    [TestCase(0f, false)]
    public void ABallOnAGentleSlope_IsHeldByItsRollingResistance(float resistance, bool held)
    {
        var slope = Quaternion.CreateFromAxisAngle(Vector3F.UnitZ, 5f * MathF.PI / 180f);
        var scene = new SceneBuilder();
        scene.AddGround(rotation: slope);
        var start = Vector3F.Transform(new Vector3F(0, 0.5f, 0), slope);
        var ball = scene.World.Add(new BodyDescription
        {
            Shape = new SphereCollisionShape(0.5f),
            Position = new WorldPosition(start),
            Material = new PhysicsMaterial { RollingResistance = resistance, FrictionCombine = CombineRule.Maximum },
            CanSleep = false
        });

        scene.Run(3f);

        var moved = (scene.World.GetPose(ball).Position - new WorldPosition(start)).Length();
        if (held)
            Assert.That(moved, Is.LessThan(0.001f));
        else
            Assert.That(moved, Is.GreaterThan(1f));
    }

    // A crate dropped on a floor of ice tells the ice it touched, and a ray down meets the ice - its part's own and
    // the body's alike.
    [Test]
    public void TheSurfaceOfAMaterial_IsToldWithItsContactsAndHits()
    {
        using var simulation = new PhysicsSimulation();
        var floor = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Material = Ice,
            Parts =
            [
                new BodyPart { Shape = Half, Pose = new LocalPose(new Vector3F(-5, -0.5f, 0)) },
                new BodyPart
                {
                    Shape = Half, Pose = new LocalPose(new Vector3F(5, -0.5f, 0)),
                    Material = new PhysicsMaterial { Surface = "Asphalt" }
                }
            ]
        });
        var crate = simulation.AddBody(new BodySettings { Shape = Crate, Position = new WorldPosition(-5, 1, 0) });
        var told = new List<ContactEventArgs>();
        for (var step = 0; step < simulation.Settings.StepRate; step++)
        {
            simulation.Step();
            told.AddRange(simulation.Contacts.ToArray());
        }

        simulation.RayCast(new WorldPosition(-5, 5, 2), -Vector3F.UnitY, 10, out var onIce);
        simulation.RayCast(new WorldPosition(5, 5, 0), -Vector3F.UnitY, 10, out var onAsphalt);
        simulation.CastShape(new SphereCollisionShape(0.2f), new Pose(new WorldPosition(5, 5, 0)), -Vector3F.UnitY * 10,
            out var cast);

        Assert.Multiple(() =>
        {
            Assert.That(told, Has.Count.EqualTo(1));
            Assert.That(told[0].Involves(crate, out _), Is.True);
            Assert.That(told[0].First == floor ? told[0].FirstSurface : told[0].SecondSurface, Is.EqualTo("Ice"));
            Assert.That(told[0].First == crate ? told[0].FirstSurface : told[0].SecondSurface, Is.Null);
            Assert.That(onIce.Body, Is.EqualTo(floor));
            Assert.That(simulation.SurfaceOf(onIce.Body, onIce.Part), Is.EqualTo("Ice"));
            Assert.That(simulation.SurfaceOf(onAsphalt.Body, onAsphalt.Part), Is.EqualTo("Asphalt"));
            Assert.That(simulation.SurfaceOf(cast.Body, cast.Part), Is.EqualTo("Asphalt"));
            Assert.That(simulation.SurfaceOf(crate, 0), Is.Null);
            Assert.That(simulation.SurfaceOf(floor, 7), Is.Null, "no such part");
        });

        simulation.RemoveBody(floor);
        Assert.That(simulation.SurfaceOf(floor, 0), Is.Null, "taken out, before the step that takes it");
    }

    // While its thread steps, queries meet the bodies' copies: those copies are of the body's material, and follow it.
    [Test]
    public void WhileItRuns_AHitTellsTheSurfaceOfTheBodysMaterial_AsItWasSet()
    {
        using var simulation = new PhysicsSimulation();
        var floor = simulation.AddBody(new BodySettings
        {
            Kind = BodyKind.Static, Shape = Half, Position = new WorldPosition(0, -0.5, 0), Material = Ice
        });
        simulation.Start();
        UpdateUntil(simulation, () => simulation.StatusOf(floor) == PhysicsStatus.Active);
        var met = simulation.RayCast(new WorldPosition(0, 5, 0), -Vector3F.UnitY, 10, out var hit);
        var before = simulation.SurfaceOf(hit.Body, hit.Part);

        simulation.SetMaterial(floor, Ice with { Surface = "Snow" });
        UpdateUntil(simulation, () => simulation.SurfaceOf(floor, 0) == "Snow");
        simulation.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(met, Is.True);
            Assert.That(before, Is.EqualTo("Ice"));
        });
    }

    private static BodyId AddSlidingCrate(PhysicsSimulation simulation, double x, float speed = 2) =>
        simulation.AddBody(new BodySettings
        {
            Shape = Crate, Position = new WorldPosition(x, 0.5, 0), LinearVelocity = new Vector3F(speed, 0, 0),
            CanSleep = false
        });

    private static void StepFor(PhysicsSimulation simulation, float seconds)
    {
        for (var step = 0; step < seconds * simulation.Settings.StepRate; step++)
            simulation.Step();
    }

    private static void UpdateUntil(PhysicsSimulation simulation, Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "the simulation never got there");
            simulation.Update();
        }
    }
}
