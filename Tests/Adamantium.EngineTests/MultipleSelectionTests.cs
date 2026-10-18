using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Tools;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>Several entities selected at once: a frame across the screen selects them CAD style, Shift adds and
/// Control takes out, and the move tool moves them together.</summary>
[TestFixture]
public class MultipleSelectionTests
{
    private TestUniverse universe;
    private TestOutput output;
    private Camera camera;
    private Selection selection;
    private TransformService transforms;
    private ToolsService tools;
    private Entity left;
    private Entity right;

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

        left = Cube("Left", new Vector3(-3, 0, 15));
        right = Cube("Right", new Vector3(3, 0, 15));

        transforms = new TransformService(universe.EntityWorld);
        transforms.Initialize();
        tools = new ToolsService(universe.EntityWorld);
        tools.Initialize();
        Frame();
    }

    private Entity Cube(string name, Vector3 at)
    {
        var cube = new Entity(null, name);
        cube.Transform.Position = at;
        cube.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, 2.0, 2.0, 2.0) });
        var collider = new BoxCollider();
        cube.AddComponent(collider);
        collider.Initialize();
        universe.EntityWorld.EntityManager.AddEntity(cube);
        return cube;
    }

    [Test]
    public void TheSelection_HoldsSeveral_TheLastCurrent_AndMarksThem()
    {
        selection.Select([left, right]);
        var both = selection.Items.Count;
        selection.Remove([right]);

        Assert.Multiple(() =>
        {
            Assert.That(both, Is.EqualTo(2));
            Assert.That(selection.Current, Is.SameAs(left));
            Assert.That(left.IsSelected, Is.True);
            Assert.That(right.IsSelected, Is.False);
        });
    }

    [Test]
    public void AFrameToTheRight_TakesOnlyWhatLiesWhollyInside()
    {
        var leftPixel = PixelOf(left.Transform.Position);
        var rightPixel = PixelOf(right.Transform.Position);
        var into = new List<Entity>();

        // Round the left cube, and over half the right one.
        tools.PickEntities(camera, new ScreenFrame(leftPixel - new Vector2F(120), rightPixel + new Vector2F(0, 120)), into);

        Assert.That(into, Is.EquivalentTo(new[] { left }));
    }

    // A thin strip across both, no corner of either inside it, its middle between them.
    [Test]
    public void AFrameToTheLeft_TakesWhateverItTouches()
    {
        var leftPixel = PixelOf(left.Transform.Position);
        var rightPixel = PixelOf(right.Transform.Position);
        var into = new List<Entity>();

        tools.PickEntities(camera, new ScreenFrame(rightPixel + new Vector2F(0, 5), leftPixel - new Vector2F(0, 5)), into);

        Assert.That(into, Is.EquivalentTo(new[] { left, right }));
    }

    [Test]
    public void DrawingAFrameWithTheSelectTool_SelectsWhatItTakes()
    {
        tools.Tool = new SelectTool();
        Frame();
        var from = PixelOf(left.Transform.Position) - new Vector2F(120);
        var to = PixelOf(right.Transform.Position) + new Vector2F(120);

        output.Pointer = from;
        Frame();
        output.Button(MouseButton.Left, InputType.Down);
        Frame();
        output.Pointer = to;
        Frame();
        output.Button(MouseButton.Left, InputType.Up);
        Frame();

        Assert.That(selection.Items, Is.EquivalentTo(new[] { left, right }));
    }

    [Test]
    public void TheMoveTool_MovesEverythingSelected_Together()
    {
        selection.Select([left, right]);
        tools.Tool = new MoveTool();
        Frame();
        var startLeft = left.Transform.Position;
        var startRight = right.Transform.Position;
        var center = (startLeft + startRight) / 2;

        // The arrow along X runs to the right of the center as this camera looks.
        var grab = PixelOf(center) + new Vector2F(60, 0);
        output.Pointer = grab;
        Frame();
        output.Button(MouseButton.Left, InputType.Down);
        Frame();
        output.Pointer = grab + new Vector2F(40, 0);
        Frame();

        var movedLeft = left.Transform.Position - startLeft;
        var movedRight = right.Transform.Position - startRight;
        Assert.Multiple(() =>
        {
            Assert.That(movedLeft.Length(), Is.GreaterThan(0.1), "they move");
            Assert.That((movedLeft - movedRight).Length(), Is.LessThan(1e-4), "together");
        });
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
