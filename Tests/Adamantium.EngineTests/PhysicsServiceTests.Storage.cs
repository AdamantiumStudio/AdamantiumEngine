using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Adamantium.DarkMatter;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Physics;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>What the game's physics takes from the project and the entities' settings: the project's layers and which
/// collide, its gravity, a body's and a collider's layer and material file, a trigger.</summary>
public partial class PhysicsServiceTests
{
    // Ghosts pass through the default layer: a ghost falls through the ground, a box beside it rests on it.
    [Test]
    public void ABodyOnALayerKeptApart_FallsThroughTheGround()
    {
        UseSteppedPhysics();
        _physics.Project = Ghostly();
        AddGround();
        var ghost = AddBox("Ghost", new WorldPosition(0, -1, 0), 0.5);
        ghost.GetComponent<RigidBody>().Layer = "Ghosts";
        var box = AddBox("Box", new WorldPosition(3, -1, 0), 0.5);
        Play();

        Frames(1.5);

        Assert.Multiple(() =>
        {
            Assert.That(ghost.Transform.Position.Y, Is.GreaterThan(2), "through the ground");
            Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
        });
    }

    // A collider of its own layer on a body of the default one: that part passes through, the body falls.
    [Test]
    public void AColliderOnALayerKeptApart_PassesThroughTheGround()
    {
        UseSteppedPhysics();
        _physics.Project = Ghostly();
        AddGround();
        var ghost = AddBox("Ghost", new WorldPosition(0, -1, 0), 0.5);
        ghost.GetComponent<BoxCollider>().Layer = "Ghosts";
        Play();

        Frames(1.5);

        Assert.That(ghost.Transform.Position.Y, Is.GreaterThan(2));
    }

    // A box resting on a solid zone; the zone made a trigger during the game: the box falls through it.
    [Test]
    public void AColliderMadeATriggerDuringTheGame_LetsWhatRestsOnItFall()
    {
        UseSteppedPhysics();
        var zone = MakeBox("Zone", new WorldPosition(0, 0, 0), 1);
        zone.GetComponent<RigidBody>().Kind = BodyKind.Static;
        _world.EntityManager.AddEntity(zone, _world.MainScene);
        Settle();
        var box = AddBox("Box", new WorldPosition(0, -1.5, 0), 0.5);
        Play();
        Frames(1);
        var resting = box.Transform.Position.Y;

        zone.GetComponent<BoxCollider>().IsTrigger = true;
        Frames(1);

        Assert.Multiple(() =>
        {
            Assert.That(resting, Is.EqualTo(-1.5).Within(0.02), "resting on the solid zone first");
            Assert.That(box.Transform.Position.Y, Is.GreaterThan(1), "through it once a trigger");
        });
    }

    // A collider made of another kind keeps being a trigger, on its layer, of its material.
    [Test]
    public void AColliderOfAnotherKind_KeepsItsTraits()
    {
        var part = MakeBox("Part", WorldPosition.Zero, 0.5);
        part.AddComponent(new MeshData { Mesh = FloorMesh(0.5f) });
        var collider = part.GetComponent<BoxCollider>();
        collider.IsTrigger = true;
        collider.Layer = "Zones";
        collider.MaterialPath = "Materials/Ice" + PhysicsMaterialFile.Extension;

        PhysicsParts.SetCollider(part, ColliderKind.Sphere);

        Assert.That(PhysicsParts.TraitsOf(part.GetComponent<Collider>()), Is.EqualTo(new ColliderTraits
        {
            IsTrigger = true, Layer = "Zones", MaterialPath = "Materials/Ice" + PhysicsMaterialFile.Extension
        }));
    }

    // A project the game cannot take is refused when given, not when the game starts.
    [Test]
    public void AProjectAGameCannotTake_IsRefused()
    {
        UseSteppedPhysics();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => _physics.Project = new ProjectPhysics { StepRate = 0 });
            Assert.Throws<ArgumentException>(() =>
                _physics.Project = new ProjectPhysics { Gravity = new Vector3F(float.NaN, 0, 0) });
        });
    }

    // A layer the project has not: said in the log, the body stays on the default layer and lands.
    [Test]
    public void ABodyOnALayerTheProjectHasNot_StaysOnTheDefault()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new WorldPosition(0, -1, 0), 0.5);
        box.GetComponent<RigidBody>().Layer = "Nowhere";
        Play();

        Frames(1.5);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    // A zone made a trigger: a box falls through it onto the ground, the zone told it entered.
    [Test]
    public void ATriggerCollider_IsEnteredAndPushesNothing()
    {
        UseSteppedPhysics();
        AddGround();
        var zone = MakeBox("Zone", new WorldPosition(0, -1.5, 0), 1);
        zone.GetComponent<RigidBody>().Kind = BodyKind.Static;
        zone.GetComponent<BoxCollider>().IsTrigger = true;
        _world.EntityManager.AddEntity(zone, _world.MainScene);
        Settle();
        var box = AddBox("Box", new WorldPosition(0, -4, 0), 0.5);
        Play();
        var told = new List<EntityContactEventArgs>();
        for (var frame = 0; frame < 120; frame++)
        {
            Frame(FrameSeconds);
            told.AddRange(_physics.Contacts.ToArray());
        }

        Assert.Multiple(() =>
        {
            Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02), "through the zone, onto the ground");
            Assert.That(told.Exists(contact => contact.IsTrigger && contact.Change == DarkMatter.ContactChange.Began
                                               && contact.Involves(zone, out var other) && other == box), Is.True);
        });
    }

    // A trigger weighs nothing: a body of a box and a trigger beside it weighs as the box alone, as the physics does.
    [Test]
    public void ATriggersVolume_IsNotWeighed()
    {
        var box = MakeBox("Box", WorldPosition.Zero, 0.5);
        var alone = box.GetComponent<RigidBody>().ComputedMass;
        AddPart(box, "Sensor", new WorldPosition(2, 0, 0), new Vector3<double>(1)).GetComponent<BoxCollider>()
            .IsTrigger = true;

        Assert.That(box.GetComponent<RigidBody>().ComputedMass, Is.EqualTo(alone).Within(1e-3f));
    }

    // A ball of a bouncy material file dropped on the ground bounces back up; one of none rests.
    [Test]
    public void ABodysMaterialFile_IsWhatItBouncesBy()
    {
        var folder = Directory.CreateTempSubdirectory("adamantium-physmat-");
        try
        {
            var bouncy = Path.Combine(folder.FullName, "Bouncy" + PhysicsMaterialFile.Extension);
            File.WriteAllText(bouncy, "<PhysicsMaterial Version=\"1\" Restitution=\"0.9\" RestitutionCombine=\"Maximum\" />");
            var assets = new AssetService(_universe.Container, _universe.Satellites);
            assets.Resolvers.Add(new FileSystemAssetResolver());
            assets.Readers.Add(typeof(PhysicsMaterial), new PhysicsMaterialAssetReader());
            _universe.Satellites.Add<IAssetService>(assets);
            UseSteppedPhysics();
            AddGround();
            var ball = AddBox("Ball", new WorldPosition(0, -3, 0), 0.5);
            ball.GetComponent<RigidBody>().MaterialPath = bouncy;
            var plain = AddBox("Plain", new WorldPosition(3, -3, 0), 0.5);
            Play();
            var bounced = false;
            for (var frame = 0; frame < 90 && !bounced; frame++)
            {
                Frame(FrameSeconds);
                bounced = _physics.GetLinearVelocity(ball).Y < -2;
            }

            Assert.Multiple(() =>
            {
                Assert.That(bounced, Is.True, "back up, against +Y down");
                Assert.That(_physics.GetLinearVelocity(plain).Y, Is.GreaterThan(-0.5f), "the plain one did not bounce");
            });
        }
        finally
        {
            folder.Delete(true);
        }
    }

    // Ground of a material file naming its surface: a box landing on it is told the ground's surface, and a ray meets it.
    [Test]
    public void TheSurfaceOfAMaterialFile_IsToldWithEntityContactsAndHits()
    {
        var folder = Directory.CreateTempSubdirectory("adamantium-physmat-");
        try
        {
            var asphalt = Path.Combine(folder.FullName, "Asphalt" + PhysicsMaterialFile.Extension);
            File.WriteAllText(asphalt, "<PhysicsMaterial Version=\"2\" Surface=\"Asphalt\" />");
            var assets = new AssetService(_universe.Container, _universe.Satellites);
            assets.Resolvers.Add(new FileSystemAssetResolver());
            assets.Readers.Add(typeof(PhysicsMaterial), new PhysicsMaterialAssetReader());
            _universe.Satellites.Add<IAssetService>(assets);
            UseSteppedPhysics();
            var ground = AddGround();
            ground.GetComponent<RigidBody>().MaterialPath = asphalt;
            var box = AddBox("Box", new WorldPosition(0, -1.5, 0), 0.5);
            Play();
            var told = new List<EntityContactEventArgs>();
            for (var frame = 0; frame < 60; frame++)
            {
                Frame(FrameSeconds);
                told.AddRange(_physics.Contacts.ToArray());
            }

            var met = _physics.RayCast(new WorldPosition(5, -10, 0), Vector3F.UnitY, 20, out var hit);

            var began = told.Find(contact => contact.Change == ContactChange.Began);
            Assert.Multiple(() =>
            {
                Assert.That(began.Involves(box, out var other) && other == ground, Is.True);
                Assert.That(began.First == ground ? began.FirstSurface : began.SecondSurface, Is.EqualTo("Asphalt"));
                Assert.That(began.First == box ? began.FirstSurface : began.SecondSurface, Is.Null);
                Assert.That(met && hit.Entity == ground, Is.True);
                Assert.That(hit.Surface, Is.EqualTo("Asphalt"));
            });
        }
        finally
        {
            folder.Delete(true);
        }
    }

    // A material file that is not there: said in the log, the body rubs and bounces by the default and lands.
    [Test]
    public void AMaterialFileNotThere_LeavesTheDefault()
    {
        UseSteppedPhysics();
        AddGround();
        var box = AddBox("Box", new WorldPosition(0, -1, 0), 0.5);
        box.GetComponent<RigidBody>().MaterialPath = "Materials/Nowhere" + PhysicsMaterialFile.Extension;
        Play();

        Frames(1.5);

        Assert.That(box.Transform.Position.Y, Is.EqualTo(-0.5).Within(0.02));
    }

    // The project's gravity holds in its game: none, and a box stays where it is; another game without it falls.
    [Test]
    public void TheProjectsGravity_IsTheGamesOwn()
    {
        UseSteppedPhysics();
        _physics.Project = new ProjectPhysics { Gravity = Vector3F.Zero };
        var box = AddBox("Box", new WorldPosition(0, -3, 0), 0.5);
        Play();

        Frames(1);

        Assert.Multiple(() =>
        {
            Assert.That(box.Transform.Position.Y, Is.EqualTo(-3).Within(1e-6));
            Assert.Throws<InvalidOperationException>(() => _physics.Project = null, "not during a game");
        });
    }

    // A ray down through a ghost and a box beneath: on the ghosts' layer alone it meets the ghost.
    [Test]
    public void AQueryOnALayer_MeetsOnlyItsBodies()
    {
        UseSteppedPhysics();
        _physics.Project = Ghostly();
        var ghost = MakeBox("Ghost", new WorldPosition(0, -3, 0), 0.5);
        ghost.GetComponent<RigidBody>().Kind = BodyKind.Static;
        ghost.GetComponent<RigidBody>().Layer = "Ghosts";
        _world.EntityManager.AddEntity(ghost, _world.MainScene);
        var box = MakeBox("Box", new WorldPosition(0, -1, 0), 0.5);
        box.GetComponent<RigidBody>().Kind = BodyKind.Static;
        _world.EntityManager.AddEntity(box, _world.MainScene);
        Settle();
        Play();
        Frame(FrameSeconds);
        var from = new WorldPosition(0, 5, 0);

        var metGhost = _physics.Query().OnLayers(LayerMask.Of(_physics.FindLayer("Ghosts")))
            .RayCast(from, -Vector3F.UnitY, 20, out var onGhosts);
        var metBox = _physics.Query().OnLayers(LayerMask.Of(_physics.FindLayer("Default")))
            .RayCast(from, -Vector3F.UnitY, 20, out var onDefault);

        Assert.Multiple(() =>
        {
            Assert.That(metGhost && onGhosts.Entity == ghost, Is.True);
            Assert.That(metBox && onDefault.Entity == box, Is.True);
            Assert.Throws<ArgumentException>(() => _physics.FindLayer("Nowhere"));
        });
    }

    [Test]
    public void AProjectsPhysicsFile_IsReadAsWritten()
    {
        const string text = """
                            <ProjectPhysics Version="1" Gravity="0, 3.71, 0" StepRate="240">
                              <Layer Name="Debris" />
                              <Layer Name="Ghosts" />
                              <Apart First="Ghosts" Second="Default" />
                            </ProjectPhysics>
                            """;

        var physics = ProjectPhysics.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        Assert.Multiple(() =>
        {
            Assert.That(physics.Gravity, Is.EqualTo(new Vector3F(0, 3.71f, 0)));
            Assert.That(physics.StepRate, Is.EqualTo(240));
            Assert.That(physics.Layers.ConvertAll(layer => layer.Name), Is.EqualTo((string[])["Debris", "Ghosts"]));
            Assert.That(physics.Apart[0].First + "/" + physics.Apart[0].Second, Is.EqualTo("Ghosts/Default"));
            var later = Refusal(() => ProjectPhysics.Read(Stream("<ProjectPhysics Version=\"9\" />")));
            Assert.That((later.Problem, later.Version, later.ReadsUpTo),
                Is.EqualTo((PhysicsFileProblem.LaterVersion, 9, ProjectPhysics.CurrentVersion)));
            Assert.That(Refusal(() => ProjectPhysics.Read(Stream("<Scene />"))).Problem,
                Is.EqualTo(PhysicsFileProblem.NotThisKind));
            var noSteps = Refusal(() => ProjectPhysics.Read(Stream("<ProjectPhysics StepRate=\"0\" />")));
            Assert.That((noSteps.Problem, noSteps.Settings),
                Is.EqualTo((PhysicsFileProblem.SettingsRefused, PhysicsSettingsProblem.NoSteps)));
        });
    }

    // Why a physics file is not read, as values a reader says in its own words.
    private static PhysicsFileException Refusal(Func<object> read) =>
        Assert.Throws<PhysicsFileException>(() => read());

    private static PhysicsSettingsProblem SettingsRefusal(string file)
    {
        var refusal = Refusal(() => ProjectPhysics.Read(Stream(file)));
        Assert.That(refusal.Problem, Is.EqualTo(PhysicsFileProblem.SettingsRefused));
        return refusal.Settings;
    }

    // The solver's settings come from the file; one that leaves them out steps as a simulation made with none.
    [Test]
    public void TheSolversSettingsInAProjectsFile_AreTheSettingsItGives()
    {
        var set = ProjectPhysics.Read(Stream(
            "<ProjectPhysics Version=\"1\" Gravity=\"0, 1, 0\" StepRate=\"60\" Substeps=\"8\" MaxContactPointsPerPair=\"2\" "
            + "SpeculativeDistance=\"0.05\" ContactHertz=\"45\" ContactDampingRatio=\"2\" MaxPushOutSpeed=\"5\" "
            + "RestitutionThreshold=\"0.5\" BodiesSleep=\"false\" SleepSpeed=\"0.1\" TimeToSleep=\"2\" "
            + "MaxLinearSpeed=\"300\" MaxStepsPerAdvance=\"4\" />")).ToSettings();
        var left = ProjectPhysics.Read(Stream("<ProjectPhysics Version=\"1\" Gravity=\"0, 1, 0\" />")).ToSettings();

        Assert.Multiple(() =>
        {
            Assert.That(set, Is.EqualTo(new PhysicsSettings
            {
                Gravity = new Vector3F(0, 1, 0), StepRate = 60, Substeps = 8, MaxContactPointsPerPair = 2,
                SpeculativeDistance = 0.05f, ContactHertz = 45, ContactDampingRatio = 2, MaxPushOutSpeed = 5,
                RestitutionThreshold = 0.5f, SleepSpeed = 0.1f, TimeToSleep = float.PositiveInfinity,
                MaxLinearSpeed = 300, MaxStepsPerAdvance = 4
            }));
            Assert.That(left, Is.EqualTo(new PhysicsSettings { Gravity = new Vector3F(0, 1, 0) }));
            Assert.That(SettingsRefusal("<ProjectPhysics Substeps=\"0\" />"), Is.EqualTo(PhysicsSettingsProblem.NoSubsteps));
            Assert.That(SettingsRefusal("<ProjectPhysics MaxContactPointsPerPair=\"99\" />"),
                Is.EqualTo(PhysicsSettingsProblem.ContactPointsOutOfRange));
            Assert.That(Refusal(() => ProjectPhysics.Read(Stream("<ProjectPhysics TimeToSleep=\"INF\" />"))).Problem,
                Is.EqualTo(PhysicsFileProblem.TimeToSleepNotFinite));
            Assert.That(SettingsRefusal("<ProjectPhysics MaxStepsPerAdvance=\"0\" />"),
                Is.EqualTo(PhysicsSettingsProblem.NoStepsPerAdvance));
            Assert.That(SettingsRefusal("<ProjectPhysics BodiesSleep=\"false\" TimeToSleep=\"-5\" />"),
                Is.EqualTo(PhysicsSettingsProblem.TimeToSleepOutOfRange), "kept with sleeping off, to come back on");
            Assert.That(Refusal(() => ProjectPhysics.Read(Stream("<ProjectPhysics StepRate=\"many\" />"))).Problem,
                Is.EqualTo(PhysicsFileProblem.ValueNotRead), "a project's physics, with a value that is no number");
        });
    }

    // A project whose bodies do not sleep: a box at rest on the ground stays awake, where it would sleep by default.
    [Test]
    public void AProjectWhoseBodiesDoNotSleep_KeepsABoxAtRestAwake()
    {
        UseSteppedPhysics();
        _physics.Project = new ProjectPhysics { BodiesSleep = false };
        AddGround();
        AddBox("Box", new WorldPosition(0, -0.6, 0), 0.5);
        Play();

        Frames(3);

        Assert.That(_physics.LastStep.AwakeBodies, Is.EqualTo(1));
    }

    [Test]
    public void APhysicsMaterialFile_IsReadAsWritten_AndAMaterialNoneCanHaveIsRefused()
    {
        var material = PhysicsMaterialFile.Read(Stream(
            "<PhysicsMaterial Version=\"2\" StaticFriction=\"0.7\" DynamicFriction=\"0.05\" RollingResistance=\"0.02\" "
            + "Restitution=\"0.3\" FrictionCombine=\"Minimum\" Surface=\" Ice \" />")).ToMaterial();

        Assert.Multiple(() =>
        {
            Assert.That(material.StaticFriction, Is.EqualTo(0.7f));
            Assert.That(material.DynamicFriction, Is.EqualTo(0.05f));
            Assert.That(material.RollingResistance, Is.EqualTo(0.02f));
            Assert.That(material.Surface, Is.EqualTo("Ice"));
            Assert.That(material.Restitution, Is.EqualTo(0.3f));
            Assert.That(material.FrictionCombine, Is.EqualTo(CombineRule.Minimum));
            Assert.That(material.RestitutionCombine, Is.EqualTo(PhysicsMaterial.Default.RestitutionCombine));
            Assert.That(Refusal(() => PhysicsMaterialFile.Read(Stream("<PhysicsMaterial Friction=\"-1\" />"))).Problem,
                Is.EqualTo(PhysicsFileProblem.MaterialAmountRefused));
            Assert.That(Refusal(() => PhysicsMaterialFile.Read(Stream("<PhysicsMaterial RollingResistance=\"-1\" />")))
                .Problem, Is.EqualTo(PhysicsFileProblem.MaterialAmountRefused));
            Assert.That(Refusal(() => PhysicsMaterialFile.Read(Stream("<PhysicsMaterial FrictionCombine=\"Sideways\" />")))
                .Problem, Is.EqualTo(PhysicsFileProblem.ValueNotRead), "a choice there is none of");
            Assert.That(Refusal(() => PhysicsMaterialFile.Read(Stream("<PhysicsMaterial"))).Problem,
                Is.EqualTo(PhysicsFileProblem.NotThisKind), "damaged XML");
        });
    }

    [Test]
    public void AFirstVersionMaterialsFriction_IsReadAsBothFrictions_AndWrittenAsBoth()
    {
        var file = PhysicsMaterialFile.Read(Stream("<PhysicsMaterial Version=\"1\" Friction=\"0.05\" />"));
        using var written = new MemoryStream();
        file.Write(written);
        var text = Encoding.UTF8.GetString(written.ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(file.ToMaterial().StaticFriction, Is.EqualTo(0.05f));
            Assert.That(file.ToMaterial().DynamicFriction, Is.EqualTo(0.05f));
            Assert.That(file.ToMaterial().Surface, Is.Null);
            Assert.That(text, Does.Contain("StaticFriction=\"0.05\"").And.Contain("DynamicFriction=\"0.05\""));
            Assert.That(text, Does.Not.Contain(" Friction="));
            Assert.That(text, Does.Contain("Version=\"2\""));
        });
    }

    // A file of the second version is read by its own frictions, a first version's Friction in it left alone wherever it
    // stands; a file naming no version is of the first. A blank surface is none, one written with spaces is kept without.
    [Test]
    public void ASecondVersionMaterial_IsReadByItsOwnFrictions_AndItsSurfaceTrimmed()
    {
        var friction = PhysicsMaterialFile.Read(Stream(
            "<PhysicsMaterial Friction=\"0.1\" Version=\"2\" StaticFriction=\"0.9\" DynamicFriction=\"0.7\" />"));
        var unversioned = PhysicsMaterialFile.Read(Stream("<PhysicsMaterial Friction=\"0.1\" />"));
        var file = PhysicsMaterialFile.From(new PhysicsMaterial { Surface = " Ice " });

        Assert.Multiple(() =>
        {
            Assert.That(friction.StaticFriction, Is.EqualTo(0.9f));
            Assert.That(friction.DynamicFriction, Is.EqualTo(0.7f));
            Assert.That(unversioned.Version, Is.EqualTo(1));
            Assert.That(unversioned.StaticFriction, Is.EqualTo(0.1f));
            Assert.That(file.Surface, Is.EqualTo("Ice"));
            Assert.That(new PhysicsMaterialFile { Surface = "  " }.ToMaterial().Surface, Is.Null);
        });
    }

    private static ProjectPhysics Ghostly() => new()
    {
        Layers = [new ProjectLayer { Name = "Ghosts" }],
        Apart = [new LayerPair { First = "Ghosts", Second = "Default" }]
    };

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
}
