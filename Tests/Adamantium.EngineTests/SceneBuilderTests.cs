using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Scenes;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using NUnit.Framework;
using CameraType = Adamantium.Graphics.Core.CameraType;

namespace Adamantium.EngineTests;

/// <summary>A scene put into a world and written back from it is the scene it was: models by their assets and where
/// they stand, nothing of their parts; lights, cameras and spawn points whole; the game's components with their data,
/// those whose code is not loaded as they were; the editor's own cameras left out.</summary>
[TestFixture]
public class SceneBuilderTests
{
    private const string RobotAsset = "Models/Robot/robot.glb";
    private const string MissingAsset = "Models/Gone/gone.fbx";

    private TestUniverse universe;
    private CountingAssets assets;

    [SetUp]
    public void SetUp()
    {
        universe = new TestUniverse();
        universe.Satellites.Add<IUniverse>(universe);
        assets = new CountingAssets();
        assets.Models[RobotAsset] = Robot();
    }

    private static SceneData Robot()
    {
        var scene = new SceneData { Name = "robot.glb", Source = RobotAsset };
        scene.Models = scene.CreateMesh(null, "", "Robot");
        var body = scene.CreateMesh(scene.Models, "body", "Body");
        body.Meshes.Add(new Mesh(PrimitiveType.TriangleList).SetPoints(
            [new Vector3<double>(-1, -1, -1), new Vector3<double>(1, 1, 1)]));
        return scene;
    }

    private static SceneFile Sample()
    {
        var scene = new SceneFile();
        scene.Models.Add(new SceneModel
        {
            Uid = 1, Name = "Robot A", Asset = RobotAsset, Position = new Vector3<double>(4, 0, -2),
            Rotation = new QuaternionF(0, 0.7071068f, 0, 0.7071068f), Scale = new Vector3F(2, 2, 2)
        });
        scene.Models.Add(new SceneModel { Uid = 2, Name = "Robot B", Asset = RobotAsset, Position = new Vector3<double>(-4, 0, 0) });
        scene.Lights.Add(new SceneLight
        {
            Uid = 10, Name = "Lamp", Type = LightType.Spot, Parent = 1, Position = new Vector3<double>(0, 3, 0),
            Color = new Vector3F(1, 0.5f, 0.25f), Intensity = 3, Range = 15, InnerSpotAngle = 20, OuterSpotAngle = 35,
            Shadows = true, ShadowMapResolution = 1024
        });
        scene.Cameras.Add(new SceneCamera
        {
            Uid = 20, Name = "Intro", Position = new Vector3<double>(10, 4, 10), FieldOfView = 50, Near = 0.5f,
            Far = 900, Type = CameraType.ThirdPersonFree, Rotation = new QuaternionF(0, 0.47942555f, 0, 0.87758255f)
        });
        scene.Markers.Add(new SceneMarker { Uid = 40, Name = "Props", Kind = SceneMarkerKind.Group, Position = new Vector3<double>(100, 0, 0) });
        var crate = new SceneShape
        {
            Uid = 50, Name = "Crate", Type = typeof(CubeShape).FullName, Parent = 40, Position = new Vector3<double>(1, 0, 0),
            Color = new Vector4F(0.5f, 0.25f, 0, 1), Scale = new Vector3F(2, 2, 2)
        };
        crate.Values.Add(new SceneValue(nameof(CubeShape.Size), "1, 0.5, 1"));
        scene.Shapes.Add(crate);
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 30, Name = "Start", Position = new Vector3<double>(0, 0, 5) });
        var patrol = new SceneBehaviour { On = 1, Type = typeof(Patrol).FullName };
        patrol.Values.Add(new SceneValue(nameof(Patrol.Speed), "2.5"));
        patrol.Values.Add(new SceneValue(nameof(Patrol.Route), "A, B"));
        patrol.Values.Add(new SceneValue(nameof(Patrol.Goal), "1, 2, 3"));
        patrol.Values.Add(new SceneValue(nameof(Patrol.Mode), nameof(PatrolMode.Loop)));
        scene.Behaviours.Add(patrol);
        var unloaded = new SceneBehaviour { On = 30, Type = "SomeMod.Unknown" };
        unloaded.Values.Add(new SceneValue("Glow", "0, 1, 0"));
        scene.Behaviours.Add(unloaded);
        return scene;
    }

    private async Task<IReadOnlyList<Entity>> Build(SceneFile scene)
    {
        var entities = await new SceneBuilder(universe.EntityWorld.MainScene, assets).Build(scene);
        universe.EntityWorld.ForceUpdate();
        return entities;
    }

    // Built into a scene of its own, a file stands there alone, and is written back with that scene's id.
    [Test]
    public async Task ASceneBuiltIntoAnotherScene_StandsThereAlone_AndIsWrittenWithItsId()
    {
        var arena = universe.EntityWorld.CreateScene("Arena", 0xA4E7A);
        await new SceneBuilder(arena, assets).Build(Sample());
        universe.EntityWorld.ForceUpdate();

        var back = SceneCapture.Capture(arena);

        Assert.Multiple(() =>
        {
            Assert.That(back.Uid, Is.EqualTo((UInt128)0xA4E7A));
            Assert.That(back.Models.Select(model => model.Name), Is.EqualTo(new[] { "Robot A", "Robot B" }));
            Assert.That(universe.EntityWorld.MainScene.RootEntities, Is.Empty);
            Assert.That(SceneCapture.Capture(universe.EntityWorld.MainScene).Models, Is.Empty);
        });
    }

    [Test]
    public async Task AScene_PutInAWorldAndWrittenBack_IsTheSceneItWas()
    {
        await Build(Sample());

        var back = SceneCapture.Capture(universe.EntityWorld.MainScene);

        Assert.Multiple(() =>
        {
            Assert.That(back.Models.Select(model => (model.Uid, model.Name, model.Asset)),
                Is.EqualTo(new[] { ((UInt128)1, "Robot A", RobotAsset), ((UInt128)2, "Robot B", RobotAsset) }));
            var robot = back.Models[0];
            Assert.That(robot.Position, Is.EqualTo(new Vector3<double>(4, 0, -2)));
            Assert.That(robot.Rotation, Is.EqualTo(new QuaternionF(0, 0.7071068f, 0, 0.7071068f)));
            Assert.That(robot.Scale, Is.EqualTo(new Vector3F(2, 2, 2)));

            var lamp = back.Lights.Single();
            Assert.That((lamp.Uid, lamp.Parent, lamp.Type), Is.EqualTo(((UInt128)10, (UInt128?)1, LightType.Spot)));
            Assert.That(lamp.Position, Is.EqualTo(new Vector3<double>(0, 3, 0)));
            Assert.That(lamp.Color, Is.EqualTo(new Vector3F(1, 0.5f, 0.25f)));
            Assert.That((lamp.Intensity, lamp.Range, lamp.Shadows, lamp.ShadowMapResolution),
                Is.EqualTo((3f, 15f, true, 1024)));
            Assert.That(lamp.InnerSpotAngle, Is.EqualTo(20).Within(1e-4));
            Assert.That(lamp.OuterSpotAngle, Is.EqualTo(35).Within(1e-4));

            var intro = back.Cameras.Single();
            Assert.That((intro.Uid, intro.Name, intro.Type), Is.EqualTo(((UInt128)20, "Intro", CameraType.ThirdPersonFree)));
            Assert.That(intro.Rotation, Is.EqualTo(new QuaternionF(0, 0.47942555f, 0, 0.87758255f)));

            var props = back.Markers.Single();
            Assert.That((props.Uid, props.Kind, props.Position), Is.EqualTo(((UInt128)40, SceneMarkerKind.Group, new Vector3<double>(100, 0, 0))));
            var crate = back.Shapes.Single();
            Assert.That((crate.Uid, crate.Parent, crate.Type), Is.EqualTo(((UInt128)50, (UInt128?)40, typeof(CubeShape).FullName)));
            Assert.That((crate.Position, crate.Scale, crate.Color),
                Is.EqualTo((new Vector3<double>(1, 0, 0), new Vector3F(2, 2, 2), new Vector4F(0.5f, 0.25f, 0, 1))));
            Assert.That(crate.Values, Does.Contain(new SceneValue(nameof(CubeShape.Size), "1, 0.5, 1")));
            Assert.That((intro.FieldOfView, intro.Near, intro.Far), Is.EqualTo((50f, 0.5f, 900f)));
            Assert.That(intro.Position, Is.EqualTo(new Vector3<double>(10, 4, 10)));

            Assert.That(back.SpawnPoints.Single().Position, Is.EqualTo(new Vector3<double>(0, 0, 5)));

            var patrol = back.Behaviours.Single(entry => entry.Type == typeof(Patrol).FullName);
            Assert.That(patrol.On, Is.EqualTo((UInt128)1));
            Assert.That(patrol.Values, Is.EquivalentTo(new[]
            {
                new SceneValue(nameof(Patrol.Speed), "2.5"), new SceneValue(nameof(Patrol.Route), "A, B"),
                new SceneValue(nameof(Patrol.Goal), "1, 2, 3"), new SceneValue(nameof(Patrol.Mode), "Loop")
            }));

            var unloaded = back.Behaviours.Single(entry => entry.Type == "SomeMod.Unknown");
            Assert.That((unloaded.On, unloaded.Values.Single()), Is.EqualTo(((UInt128)30, new SceneValue("Glow", "0, 1, 0"))));
        });
    }

    [Test]
    public async Task EachModel_IsLoadedOnce_HoweverManyTimesItStandsInTheScene()
    {
        var entities = await Build(Sample());

        Assert.Multiple(() =>
        {
            Assert.That(assets.Loads[RobotAsset], Is.EqualTo(1));
            Assert.That(entities[0].Dependencies.Select(part => part.Name), Does.Contain("Body"));
            Assert.That(entities[1].Dependencies.Select(part => part.Name), Does.Contain("Body"));
        });
    }

    [Test]
    public async Task TheGamesComponent_GetsItsDataFromTheScene()
    {
        var entities = await Build(Sample());

        var patrol = entities[0].GetComponent<Patrol>();
        Assert.Multiple(() =>
        {
            Assert.That(patrol.Speed, Is.EqualTo(2.5));
            Assert.That(patrol.Route, Is.EqualTo("A, B"));
            Assert.That(patrol.Goal, Is.EqualTo(new Vector3F(1, 2, 3)));
            Assert.That(patrol.Mode, Is.EqualTo(PatrolMode.Loop));
        });
    }

    [Test]
    public async Task AModelThatDoesNotLoad_StaysNamingItsAsset_AndTheRestOfTheSceneIsBuilt()
    {
        var scene = Sample();
        scene.Models.Add(new SceneModel { Uid = 3, Name = "Gone", Asset = MissingAsset, Position = new Vector3<double>(1, 2, 3) });

        await Build(scene);
        var back = SceneCapture.Capture(universe.EntityWorld.MainScene);

        var gone = back.Models.Single(model => model.Uid == 3);
        Assert.Multiple(() =>
        {
            Assert.That((gone.Name, gone.Asset), Is.EqualTo(("Gone", MissingAsset)));
            Assert.That(gone.Position, Is.EqualTo(new Vector3<double>(1, 2, 3)));
            Assert.That(back.Models, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task TheCameraAnOutputOwns_IsTheEditorsView_AndIsLeftOut()
    {
        var output = new OffscreenOutput(4, 4);
        var view = new Entity(null, "Main camera for the view");
        var camera = new Camera(Vector3<double>.ForwardLH, -Vector3<double>.Up, 45, 4, 4, 0.1f, 100);
        view.AddComponent(camera);
        output.OwnCamera = camera;
        universe.Add(output);
        universe.EntityWorld.EntityManager.AddEntity(view);

        await Build(Sample());
        var back = SceneCapture.Capture(universe.EntityWorld.MainScene);

        Assert.That(back.Cameras.Select(each => each.Name), Is.EqualTo(new[] { "Intro" }));
    }

    [Test]
    public async Task ParentsThatGoRoundInACircle_PutNoEntityInItself()
    {
        var scene = new SceneFile();
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 1, Name = "One", Parent = 2 });
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 2, Name = "Two", Parent = 1 });

        var entities = await Build(scene);

        Assert.That(entities.Count(entity => entity.Owner == null), Is.EqualTo(1));
    }

    [Test]
    public async Task AnIdTakenBefore_LeavesTheLaterEntryOut_AndTheSceneIsBuilt()
    {
        var scene = new SceneFile();
        scene.Models.Add(new SceneModel { Uid = 1, Name = "Robot", Asset = RobotAsset });
        scene.Cameras.Add(new SceneCamera { Uid = 1, Name = "Same id" });
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 2, Name = "Start" });
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 2, Name = "Start again" });

        var entities = await Build(scene);

        Assert.That(entities.Select(entity => entity.Name), Is.EqualTo(new[] { "Robot", "Start" }));
    }

    [Test]
    public async Task AnIdTheWorldHasAlready_LeavesTheEntryOut()
    {
        await Build(Sample());

        var again = await Build(Sample());

        Assert.Multiple(() =>
        {
            Assert.That(again, Is.Empty);
            Assert.That(SceneCapture.Capture(universe.EntityWorld.MainScene).Models, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task TwoComponentsWhoseCodeIsNotLoaded_OnOneEntity_AreBothKept()
    {
        var scene = new SceneFile();
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 1, Name = "Start" });
        scene.Behaviours.Add(new SceneBehaviour { On = 1, Type = "ModA.Glow" });
        scene.Behaviours.Add(new SceneBehaviour { On = 1, Type = "ModB.Hum" });

        await Build(scene);
        var back = SceneCapture.Capture(universe.EntityWorld.MainScene);

        Assert.That(back.Behaviours.Select(entry => entry.Type), Is.EqualTo(new[] { "ModA.Glow", "ModB.Hum" }));
    }

    [Test]
    public async Task AFieldOverridden_IsWrittenOnce_AndOneNamedAsTheEntrysOwnAttributeIsNotWritten()
    {
        var scene = new SceneFile();
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 1, Name = "Start" });
        var entry = new SceneBehaviour { On = 1, Type = typeof(FastWalker).FullName };
        entry.Values.Add(new SceneValue(nameof(FastWalker.Speed), "7"));
        scene.Behaviours.Add(entry);
        await Build(scene);

        var back = SceneCapture.Capture(universe.EntityWorld.MainScene);
        using var stream = new MemoryStream();
        back.Write(stream);

        Assert.That(back.Behaviours.Single().Values.Select(value => value.Name),
            Is.EquivalentTo(new[] { nameof(FastWalker.Speed) }));
    }

    [Test]
    public async Task WhatStandsInAPartOfAModel_IsWrittenWhereItStands()
    {
        var scene = new SceneFile();
        scene.Models.Add(new SceneModel { Uid = 1, Name = "Robot", Asset = RobotAsset });
        var entities = await Build(scene);
        var body = entities[0].Dependencies.Single(part => part.Name == "Body");
        body.Transform.Position = new Vector3<double>(0, 2, 0);
        var lamp = new Entity(null, "Lamp");
        new Adamantium.Engine.Templates.Lights.LightTemplate().Build(lamp, LightType.Point);
        lamp.Transform.Position = new Vector3<double>(0, 1, 0);
        universe.EntityWorld.EntityManager.SetOwner(lamp, body);
        universe.EntityWorld.ForceUpdate();

        var written = SceneCapture.Capture(universe.EntityWorld.MainScene).Lights.Single();

        Assert.Multiple(() =>
        {
            Assert.That(written.Parent, Is.EqualTo((UInt128?)1));
            Assert.That(written.Position.Y, Is.EqualTo(3).Within(1e-9));
        });
    }

    [Test]
    public async Task ACamerasRotation_IsTheWayItFaces_AsAnyOtherEntrys()
    {
        var facing = new QuaternionF(0, 0.47942555f, 0, 0.87758255f);
        var scene = new SceneFile();
        scene.Cameras.Add(new SceneCamera { Uid = 1, Name = "Intro", Rotation = facing });

        var entities = await Build(scene);

        Assert.That(entities[0].GetComponent<Camera>().Rotation, Is.EqualTo(QuaternionF.Conjugate(facing)));
    }

    [Test]
    public async Task ASpotsAngles_ComeBackAsTheyWere()
    {
        var scene = new SceneFile();
        for (var angle = 1; angle < 90; angle++)
        {
            scene.Lights.Add(new SceneLight { Uid = (UInt128)angle, Name = $"{angle}", Type = LightType.Spot, OuterSpotAngle = angle });
        }

        await Build(scene);
        var back = SceneCapture.Capture(universe.EntityWorld.MainScene);

        Assert.That(back.Lights.Select(light => light.OuterSpotAngle), Is.EqualTo(Enumerable.Range(1, 89).Select(a => (float)a)));
    }

    [Test]
    public async Task ARotationThatIsNone_IsTakenAsNone()
    {
        var scene = new SceneFile();
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 1, Name = "Start", Rotation = new QuaternionF(0, 0, 0, 0) });
        scene.SpawnPoints.Add(new SceneSpawnPoint { Uid = 2, Name = "Turned", Rotation = new QuaternionF(0, 1, 0, 1) });

        var entities = await Build(scene);

        Assert.Multiple(() =>
        {
            Assert.That(entities[0].Transform.Rotation, Is.EqualTo(QuaternionF.Identity));
            Assert.That(entities[1].Transform.Rotation.IsNormalized, Is.True);
        });
    }

    [Test]
    public async Task AnEntryWithNoName_GetsAnEmptyOne()
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            "<Scene Version=\"1\"><SpawnPoints><SpawnPoint Uid=\"1\" Position=\"0 0 0\" /></SpawnPoints></Scene>"));

        var entities = await Build(SceneFile.Read(stream));

        Assert.That(entities[0].Name, Is.EqualTo(""));
    }

    public class Walker : Behaviour
    {
        public virtual double Speed { get; set; }
    }

    public sealed class FastWalker : Walker
    {
        public override double Speed { get; set; }

        public string Type { get; set; } = "runner";
    }

    public enum PatrolMode
    {
        Once,
        Loop
    }

    public sealed class Patrol : Behaviour
    {
        public double Speed { get; set; }

        public string Route { get; set; }

        public Vector3F Goal { get; set; }

        public PatrolMode Mode { get; set; }

        public int Reached { get; private set; }
    }

    private sealed class CountingAssets : IAssetService
    {
        public Dictionary<string, SceneData> Models { get; } = new();

        public Dictionary<string, int> Loads { get; } = new();

        public IDependencyResolver ServiceProvider => null;

        public Satellites Satellites { get; } = new();

        public bool Exists(string assetName) => Models.ContainsKey(assetName);

        public string Resolve(string assetName) => null;

        public IReadOnlyList<AssetFileInfo> GetAssets(string folder) => [];

        public void Embed(string assetName, byte[] data)
        {
        }

        public T Load<T>(string assetName, object options = null) => LoadAsync<T>(assetName, options).Result;

        public Task<T> LoadAsync<T>(string assetName, object options = null)
        {
            Loads[assetName] = Loads.GetValueOrDefault(assetName) + 1;
            return Models.TryGetValue(assetName, out var model)
                ? Task.FromResult((T)(object)model)
                : Task.FromException<T>(new FileNotFoundException(assetName));
        }

        public object Load(Type assetType, string assetName, object options = null) => null;

        public void Unload()
        {
        }

        public bool Unload<T>(string assetName) => false;

        public bool Unload(Type assetType, string assetName) => false;
    }
}
