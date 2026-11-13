using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine;
using Adamantium.Engine.EntityServices;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Two scenes of one world in two outputs, as two scene tabs side by side: each output runs, indexes and
/// picks in its own scene alone, and a scene no output shows waits.</summary>
public class ScenesInOutputsTests
{
    private TestUniverse universe;
    private EntityWorld world;
    private Scene arena;
    private TestOutput mainOutput;
    private TestOutput arenaOutput;
    private Camera mainCamera;
    private Camera arenaCamera;
    private TransformService transforms;
    private Entity ship;
    private Entity rock;

    [SetUp]
    public void BuildScenes()
    {
        universe = new TestUniverse();
        universe.Satellites.Add<IUniverse>(universe);
        world = universe.EntityWorld;
        arena = world.CreateScene("Arena");

        mainOutput = new TestOutput { PointerOver = false, KeyboardFocused = false };
        arenaOutput = new TestOutput { PointerOver = false, KeyboardFocused = false, Scene = arena };
        universe.Add(mainOutput);
        universe.Add(arenaOutput);
        mainCamera = OwnCamera(mainOutput, "Main view");
        arenaCamera = OwnCamera(arenaOutput, "Arena view");

        ship = Cube("Ship", new Vector3<double>(0, 0, 10));
        rock = Cube("Rock", new Vector3<double>(0, 0, 10));
        world.EntityManager.AddEntity(ship);
        world.EntityManager.AddEntity(rock, arena);

        universe.Satellites.Add(new Observatory(universe, world));
        transforms = new TransformService(world);
        transforms.Initialize();
        Frame();
    }

    [Test]
    public void EachOutput_HasItsOwnSceneIndexed_AndNothingOfTheOther()
    {
        Assert.Multiple(() =>
        {
            Assert.That(world.MainScene.Index.TryGetBox(ship, out _), Is.True);
            Assert.That(world.MainScene.Index.TryGetBox(rock, out _), Is.False);
            Assert.That(arena.Index.TryGetBox(rock, out _), Is.True);
            Assert.That(arena.Index.TryGetBox(ship, out _), Is.False);
        });
    }

    [Test]
    public void TheCameraOfAnOutput_IsARootOfTheSceneItShows()
    {
        Assert.Multiple(() =>
        {
            Assert.That(arenaCamera.Owner.Scene, Is.SameAs(arena));
            Assert.That(arena.Cameras.Contains(arenaCamera.Owner), Is.True);
            Assert.That(world.MainScene.Cameras.Contains(arenaCamera.Owner), Is.False);
            Assert.That(mainCamera.Owner.Scene, Is.SameAs(world.MainScene));
        });
    }

    [Test]
    public void ASceneNoOutputShows_IsNotRun()
    {
        var hangar = world.CreateScene("Hangar");
        var crate = Cube("Crate", new Vector3<double>(0, 0, 10));
        world.EntityManager.AddEntity(crate, hangar);

        Frame();

        Assert.That(hangar.Index.Count, Is.Zero);
    }

    [Test]
    public void TheObservatory_KnowsWhichOutputShowsWhichScene()
    {
        arenaOutput.PointerOver = true;
        mainOutput.KeyboardFocused = true;
        Frame();
        var observatory = universe.Satellites.Get<Observatory>();

        Assert.Multiple(() =>
        {
            Assert.That(observatory.ShownScenes, Is.EquivalentTo(new[] { world.MainScene, arena }));
            Assert.That(observatory.OutputsOf(arena), Is.EqualTo(new[] { arenaOutput }));
            Assert.That(observatory.PointerScene, Is.SameAs(arena));
            Assert.That(observatory.KeyboardScene, Is.SameAs(world.MainScene));
        });
    }

    // The tools work in the scene under the pointer, with its selection; each scene keeps its own.
    [Test]
    public void TheTools_WorkInTheSceneUnderThePointer_WithItsSelection()
    {
        var tools = new ToolsService(world);
        tools.Initialize();
        world.MainScene.Selection.Current = ship;

        arenaOutput.PointerOver = true;
        Frame();
        tools.Update(new AppTime());

        Assert.Multiple(() =>
        {
            Assert.That(tools.Scene, Is.SameAs(arena));
            Assert.That(tools.Selection, Is.SameAs(arena.Selection));
            Assert.That(tools.Selection.Current, Is.Null);
            Assert.That(world.MainScene.Selection.Current, Is.SameAs(ship), "the other scene keeps its own");
        });
    }

    // The pointer gone from every output, the tools stay in the scene last worked in.
    [Test]
    public void TheTools_StayInTheLastScene_WithNoOutputUnderThePointer()
    {
        var tools = new ToolsService(world);
        tools.Initialize();
        arenaOutput.PointerOver = true;
        Frame();
        tools.Update(new AppTime());

        arenaOutput.PointerOver = false;
        Frame();
        tools.Update(new AppTime());

        Assert.That(tools.Scene, Is.SameAs(arena));
    }

    // An output set to show another scene takes its own camera there.
    [Test]
    public void AnOutputShowingAnotherScene_TakesItsOwnCameraAlong()
    {
        var hangar = world.CreateScene("Hangar");

        arenaOutput.Scene = hangar;
        Frame();

        Assert.Multiple(() =>
        {
            Assert.That(arenaCamera.Owner.Scene, Is.SameAs(hangar));
            Assert.That(hangar.RootEntities, Does.Contain(arenaCamera.Owner));
            Assert.That(arena.RootEntities, Does.Not.Contain(arenaCamera.Owner));
            Assert.That(hangar.Cameras.Contains(arenaCamera.Owner), Is.True);
            Assert.That(arena.Cameras.Contains(arenaCamera.Owner), Is.False);
        });
    }

    private Camera OwnCamera(TestOutput output, string name)
    {
        var entity = new CameraTemplate().BuildEntity(null, name, Vector3<double>.Zero, Vector3<double>.ForwardLH,
            -Vector3<double>.Up, 800, 600, 0.1f, 1000f);
        var camera = entity.GetComponent<Camera>();
        world.EntityManager.AddEntity(entity, output.Scene);
        output.OwnCamera = camera;
        output.Camera = camera;
        return camera;
    }

    private static Entity Cube(string name, Vector3<double> at)
    {
        var cube = new Entity(null, name);
        cube.Transform.Position = at;
        cube.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, 1, 1, 1) });
        return cube;
    }

    private void Frame()
    {
        world.ForceUpdate();
        universe.Settle();
        mainOutput.Frame();
        arenaOutput.Frame();
        mainCamera.Update(new AppTime { FrameTime = 1.0 / 60 });
        arenaCamera.Update(new AppTime { FrameTime = 1.0 / 60 });
        transforms.Update(new AppTime());
    }
}
