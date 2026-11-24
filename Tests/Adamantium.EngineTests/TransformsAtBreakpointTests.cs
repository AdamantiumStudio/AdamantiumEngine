using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>At a breakpoint, what the game moved since the last frame is placed where it is now, to be drawn there: a
/// breakpoint in the loop that moves the planets shows each moved.</summary>
public class TransformsAtBreakpointTests
{
    private TestUniverse universe;
    private TestOutput output;
    private Camera camera;
    private TransformService transforms;

    [SetUp]
    public void BuildScene()
    {
        universe = new TestUniverse();
        universe.Satellites.Add<IUniverse>(universe);
        output = new TestOutput();
        universe.Add(output);
        var cameraEntity = new CameraTemplate().BuildEntity(null, "Camera", new WorldPosition(0, 0, -10),
            Vector3<double>.ForwardLH, -Vector3<double>.Up, 800, 600, 0.1f, 1000f);
        camera = cameraEntity.GetComponent<Camera>();
        output.Camera = camera;
        universe.EntityWorld.EntityManager.AddEntity(cameraEntity);
        transforms = new TransformService(universe.EntityWorld);
        transforms.Initialize();
    }

    [Test]
    public void MovedSinceTheLastFrame_IsPlacedWhereItIsNow()
    {
        var planet = Cube("Planet", new WorldPosition(1, 0, 0));
        Frame();

        planet.Transform.Position = new WorldPosition(3, 0, 0);
        transforms.UpdateAtBreakpoint();

        Assert.That((DrawnAt(planet) - PlacedAt(planet)).Length(), Is.LessThan(1e-4f),
            $"drawn at {DrawnAt(planet)}, placed at {PlacedAt(planet)}");
    }

    private Entity Cube(string name, WorldPosition at)
    {
        var cube = new Entity(null, name);
        cube.Transform.Position = at;
        cube.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, 0.5, 0.5, 0.5) });
        universe.EntityWorld.EntityManager.AddEntity(cube);
        return cube;
    }

    private Vector3F DrawnAt(Entity entity) => entity.Transform.GetMetadata(camera).WorldMatrixF.TranslationVector;

    private Vector3F PlacedAt(Entity entity) => (entity.Transform.WorldPosition - camera.WorldPosition).As<float>();

    private void Frame()
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        output.Frame();
        camera.Update(new AppTime { FrameTime = 1.0 / 60 });
        transforms.Update(new AppTime());
    }
}
