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
            Far = 900, Follows = 2
        });
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
        var entities = await new SceneBuilder(universe.EntityWorld, assets).Build(scene);
        universe.EntityWorld.ForceUpdate();
        return entities;
    }

    [Test]
    public async Task AScene_PutInAWorldAndWrittenBack_IsTheSceneItWas()
    {
        await Build(Sample());

        var back = SceneCapture.Capture(universe.EntityWorld);

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
            Assert.That((intro.Uid, intro.Name, intro.Follows), Is.EqualTo(((UInt128)20, "Intro", (UInt128?)2)));
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
        var back = SceneCapture.Capture(universe.EntityWorld);

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
        var back = SceneCapture.Capture(universe.EntityWorld);

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
