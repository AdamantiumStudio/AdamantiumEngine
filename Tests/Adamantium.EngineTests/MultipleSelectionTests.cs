using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Tools;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
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
    public void AFrame_TakesASkinWhereItsJointCarriedIt_NotWhereItWasBound()
    {
        var skinned = CarriedSkin(out var bound, out var drawn);
        var aroundBound = new List<Entity>();
        var aroundDrawn = new List<Entity>();

        tools.PickEntities(camera, new ScreenFrame(bound - new Vector2F(100), bound + new Vector2F(100)), aroundBound);
        tools.PickEntities(camera, new ScreenFrame(drawn - new Vector2F(100), drawn + new Vector2F(100)), aroundDrawn);

        Assert.Multiple(() =>
        {
            Assert.That(aroundBound, Does.Not.Contain(skinned));
            Assert.That(aroundDrawn, Does.Contain(skinned));
        });
    }

    [Test]
    public void AClick_HitsASkinWhereItsJointCarriedIt_NotWhereItWasBound()
    {
        var skinned = CarriedSkin(out var bound, out var drawn);

        Assert.Multiple(() =>
        {
            Assert.That(tools.PickEntity(PickRay.FromCamera(camera, bound)).Entity, Is.Not.SameAs(skinned));
            Assert.That(tools.PickEntity(PickRay.FromCamera(camera, drawn)).Entity, Is.SameAs(skinned));
        });
    }

    // Drawn as it is, unbent, it is clicked where it is drawn.
    [Test]
    public void AClick_HitsASkinDrawnUnbent_WhereItWasBound()
    {
        var skinned = CarriedSkin(out var bound, out var drawn);
        skinned.GetComponent<MeshData>().RenderMode = MeshRenderMode.Static;
        // The scene's index places what is drawn as each frame draws it.
        Frame();

        Assert.Multiple(() =>
        {
            Assert.That(tools.PickEntity(PickRay.FromCamera(camera, bound)).Entity, Is.SameAs(skinned));
            Assert.That(tools.PickEntity(PickRay.FromCamera(camera, drawn)).Entity, Is.Not.SameAs(skinned));
        });
    }

    // A crossing frame over the drawn skin's middle takes it.
    [Test]
    public void ACrossingFrame_TakesASkinWhereItsJointCarriedIt()
    {
        var skinned = CarriedSkin(out var bound, out var drawn);
        var overBound = new List<Entity>();
        var overDrawn = new List<Entity>();

        tools.PickEntities(camera, new ScreenFrame(bound + new Vector2F(5), bound - new Vector2F(5)), overBound);
        tools.PickEntities(camera, new ScreenFrame(drawn + new Vector2F(5), drawn - new Vector2F(5)), overDrawn);

        Assert.Multiple(() =>
        {
            Assert.That(overBound, Does.Not.Contain(skinned));
            Assert.That(overDrawn, Does.Contain(skinned));
        });
    }

    // A triangle bound to one joint at its middle, the joint then moved five units up: the skin is drawn there.
    private Entity CarriedSkin(out Vector2F bound, out Vector2F drawn)
    {
        var skinned = new Entity(null, "Skinned");
        skinned.Transform.Position = new Vector3(0, 2.5, 15);
        var mesh = new Mesh(PrimitiveType.TriangleList);
        mesh.SetPoints([new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(0, -1, 0)]).SetIndices([0, 1, 2]);
        mesh.SetJointIndices([Vector4F.Zero, Vector4F.Zero, Vector4F.Zero]);
        mesh.SetJointWeights([Vector4F.UnitX, Vector4F.UnitX, Vector4F.UnitX]);
        skinned.AddComponent(new MeshData { Mesh = mesh, RenderMode = MeshRenderMode.Skinned });
        var joint = new Entity(skinned, "Joint");
        var skin = new Skin();
        skin.Joints.Add(joint);
        skin.InverseBindMatrices.Add(Matrix4x4F.Identity);
        skinned.AddComponent(skin);
        universe.EntityWorld.EntityManager.AddEntity(skinned);
        joint.Transform.Position = new Vector3(0, -5, 0);
        Frame();
        bound = PixelOf(skinned.Transform.Position);
        drawn = PixelOf(skinned.Transform.Position + new Vector3(0, -5, 0));
        return skinned;
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

    // A tool built on the select tool - one that deletes - is handed what a click or a frame takes, and selects none of
    // it; its click waits for the release, so a frame can start on an entity.
    [Test]
    public void AToolOnTheSelectTool_IsHandedWhatAClickOrAFrameTakes()
    {
        var tool = new TakingTool();
        tools.Tool = tool;
        Frame();

        output.Pointer = PixelOf(left.Transform.Position) - new Vector2F(120);
        Frame();
        output.Button(MouseButton.Left, InputType.Down);
        Frame();
        output.Pointer = PixelOf(right.Transform.Position) + new Vector2F(120);
        Frame();
        output.Button(MouseButton.Left, InputType.Up);
        Frame();
        var framed = new List<Entity>(tool.Taken);
        tool.Taken.Clear();

        output.Pointer = PixelOf(left.Transform.Position);
        Frame();
        output.Button(MouseButton.Left, InputType.Down);
        Frame();
        var takenWhilePressed = tool.Taken.Count;
        output.Button(MouseButton.Left, InputType.Up);
        Frame();
        var clicked = new List<Entity>(tool.Taken);
        tool.Taken.Clear();

        // Pressed on the right one and drawn to the left past the other: a frame, taking whatever it touches.
        output.Pointer = PixelOf(right.Transform.Position);
        Frame();
        output.Button(MouseButton.Left, InputType.Down);
        Frame();
        output.Pointer = PixelOf(left.Transform.Position) - new Vector2F(120);
        Frame();
        output.Button(MouseButton.Left, InputType.Up);
        Frame();

        Assert.Multiple(() =>
        {
            Assert.That(framed, Is.EquivalentTo(new[] { left, right }));
            Assert.That(takenWhilePressed, Is.Zero, "a click waits for the release");
            Assert.That(clicked, Is.EqualTo(new[] { left }));
            Assert.That(tool.Taken, Is.EquivalentTo(new[] { left, right }));
            Assert.That(selection.Items, Is.Empty);
        });
    }

    private sealed class TakingTool : SelectTool
    {
        public List<Entity> Taken { get; } = [];

        protected override bool ClicksOnRelease => true;

        protected override void Clicked(Entity entity)
        {
            if (entity != null)
            {
                Taken.Add(entity);
            }
        }

        protected override void Framed(IReadOnlyList<Entity> entities) => Taken.AddRange(entities);
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
