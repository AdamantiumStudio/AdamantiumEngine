using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Tools;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A tool dragging what a third-person camera follows - the camera rides on it.</summary>
[TestFixture]
public class ThirdPersonDragTests
{
    private TestUniverse universe;
    private TestOutput output;
    private Camera camera;
    private Selection selection;
    private TransformService transforms;
    private ToolsService tools;
    private Entity target;

    [SetUp]
    public void BuildScene()
    {
        universe = new TestUniverse();
        universe.Satellites.Add<IUniverse>(universe);
        selection = new Selection();
        universe.Satellites.Add(selection);
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));

        output = new TestOutput();
        universe.Add(output);
        var cameraEntity = new CameraTemplate().BuildEntity(null, "Camera", Vector3.Zero, Vector3.ForwardLH, -Vector3.Up,
            800, 600, 0.1f, 1000f);
        camera = cameraEntity.GetComponent<Camera>();
        output.Camera = camera;
        universe.EntityWorld.EntityManager.AddEntity(cameraEntity);

        target = new Entity(null, "Target");
        target.Transform.Position = new Vector3(0, 0, 10);
        target.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, 2.0, 2.0, 2.0) });
        var collider = new BoxCollider();
        target.AddComponent(collider);
        collider.Initialize();
        universe.EntityWorld.EntityManager.AddEntity(target);

        transforms = new TransformService(universe.EntityWorld);
        transforms.Initialize();
        tools = new ToolsService(universe.EntityWorld);
        tools.Initialize();
        Frame();
    }

    [Test]
    public void DraggingWhatTheCameraFollows_StopsWhereThePointerStops()
    {
        camera.HostChangeTime = 0;
        universe.EntityWorld.EntityManager.SetOwner(camera.Owner, target);
        camera.SetThirdPersonCamera(target, Vector3F.Zero, CameraType.ThirdPersonFree);
        selection.Current = target;
        tools.Tool = new MoveTool();
        Frame();
        var start = target.Transform.Position;

        // The move arrow along X, which runs to the left of the pivot as this camera looks.
        var grab = PixelOf(target.Transform.Pivot) + new Vector2F(-60, 0);
        output.Pointer = grab;
        Frame();
        output.Button(MouseButton.Left, InputType.Down);
        Frame();

        output.Pointer = grab + new Vector2F(-40, 0);
        Frame();
        var moved = target.Transform.Position;
        for (int i = 0; i < 5; i++)
        {
            Frame();
        }

        Assert.That((moved - start).Length(), Is.GreaterThan(0.1), "the drag moves the target");
        Assert.That((target.Transform.Position - moved).Length(), Is.LessThan(1e-4),
            "the pointer stood still, and so does the target");
    }

    private void Frame()
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        output.Frame();
        camera.Update(new AppTime { FrameTime = 1.0 / 60 });
        transforms.Update(new AppTime());
        tools.Update(new AppTime());
    }

    private Vector2F PixelOf(Vector3 world)
    {
        var inRender = (Vector3F)(world - camera.WorldPosition);
        var pixel = Vector3F.Project(inRender, 0, 0, camera.Width, camera.Height, 0, 1, camera.ViewMatrix * camera.ProjectionMatrix);
        return new Vector2F(pixel.X, pixel.Y);
    }
}
