using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine;
using Adamantium.Engine.EntityServices;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>The right button's drag with Alt orbits the camera of the output under the pointer about a point - here,
/// with nothing under the pointer, the middle of its view - and without Alt still turns it about itself.</summary>
[TestFixture]
public class CameraOrbitInputTests
{
    private TestUniverse universe;
    private TestOutput output;
    private Camera camera;
    private InputService input;
    private TransformService transforms;

    [SetUp]
    public void BuildScene()
    {
        universe = new TestUniverse();
        universe.Satellites.Add<IUniverse>(universe);
        universe.Satellites.Add(new Observatory(universe, universe.EntityWorld));
        universe.Satellites.Add(new InputActions());
        output = new TestOutput();
        universe.Add(output);
        var cameraEntity = new CameraTemplate().BuildEntity(null, "Scene camera", new WorldPosition(0, 0, -10),
            Vector3<double>.ForwardLH, -Vector3<double>.Up, 800, 600, 0.1f, 1000f);
        camera = cameraEntity.GetComponent<Camera>();
        output.Camera = camera;
        universe.EntityWorld.EntityManager.AddEntity(cameraEntity);
        transforms = new TransformService(universe.EntityWorld);
        transforms.Initialize();
        input = new InputService(universe.EntityWorld);
        Frame();
    }

    [Test]
    public void AltAndTheRightButton_OrbitTheMiddleOfTheView()
    {
        var center = camera.WorldPosition + camera.Forward * camera.FocusDistance;
        var before = InView(center);

        Drag(alt: true);

        var after = InView(center);
        Assert.Multiple(() =>
        {
            Assert.That((camera.Owner.Transform.Position - new WorldPosition(0, 0, -10)).Length(), Is.GreaterThan(0.1),
                "the camera went round");
            Assert.That(after.X, Is.EqualTo(before.X).Within(1e-3));
            Assert.That(after.Y, Is.EqualTo(before.Y).Within(1e-3));
            Assert.That(after.Z, Is.EqualTo(before.Z).Within(1e-3));
        });
    }

    // A cube under the pointer, far beyond the focus distance: the orbit goes about the point of it the pointer was on.
    [Test]
    public void WhatIsUnderThePointer_IsThePivot()
    {
        var cube = new Entity(null, "Cube");
        cube.Transform.Position = new WorldPosition(0, 0, 20);
        cube.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, 2.0, 2.0, 2.0) });
        universe.EntityWorld.EntityManager.AddEntity(cube);
        Frame();
        var face = new WorldPosition(0, 0, 19);
        var before = InView(face);

        Drag(alt: true);

        var after = InView(face);
        Assert.Multiple(() =>
        {
            // About the point the ray hit, a hair off the face's middle; about the middle of the view 10 units ahead, the
            // camera would have swung far nearer or farther.
            Assert.That((camera.Owner.Transform.Position - face).Length(), Is.EqualTo(29).Within(0.1), "as far from the cube");
            Assert.That(after.X, Is.EqualTo(before.X).Within(0.01));
            Assert.That(after.Y, Is.EqualTo(before.Y).Within(0.01));
        });
    }

    // The pivot is taken as the chord goes down, before the drag moves: a flick that leaves the cube at once still
    // orbits about the point of it the pointer was on.
    [Test]
    public void ThePivot_IsTakenAsTheChordGoesDown_BeforeTheDragMoves()
    {
        var cube = new Entity(null, "Cube");
        cube.Transform.Position = new WorldPosition(0, 0, 20);
        cube.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, 2.0, 2.0, 2.0) });
        universe.EntityWorld.EntityManager.AddEntity(cube);
        output.Pointer = new Vector2F(400, 300);
        Frame();
        output.Key(Keys.LeftAlt, InputType.Down);
        output.Button(MouseButton.Right, InputType.Down);
        Frame();
        var face = new WorldPosition(0, 0, 19);
        output.Pointer = new Vector2F(700, 300);
        output.Move(300, 0);
        Frame();

        Assert.That((camera.Owner.Transform.Position - face).Length(), Is.EqualTo(29).Within(0.1),
            "about the cube, where the pointer went down");
    }

    // A camera following a subject cannot orbit: Alt and the right button turn it about itself, as the look did.
    [Test]
    public void ACameraThatCannotOrbit_TurnsAboutItself()
    {
        var subject = new Entity(null, "Subject");
        universe.EntityWorld.EntityManager.AddEntity(subject);
        Frame();
        camera.SetThirdPersonCamera(subject, Vector3F.Zero, Adamantium.Graphics.Core.CameraType.ThirdPersonFree, distanceToObject: 10);
        Frame();
        var rotation = camera.Rotation;

        Drag(alt: true);

        Assert.That(camera.Rotation, Is.Not.EqualTo(rotation), "it turns, it does not freeze");
    }

    // An orbit, then the wheel out: the camera stays a number. (The far plane that made the owner's NaN is
    // PickRayFarPlaneTests'; this fixture's is near.)
    [TestCase(CameraOrbitStyle.Turntable, false)]
    [TestCase(CameraOrbitStyle.Trackball, false)]
    [TestCase(CameraOrbitStyle.Turntable, true)]
    [TestCase(CameraOrbitStyle.Trackball, true)]
    public void AnOrbitThenTheWheel_LeavesTheCameraWhereNumbersAre(CameraOrbitStyle style, bool overACube)
    {
        output.OrbitStyle = style;
        if (overACube)
        {
            var cube = new Entity(null, "Cube");
            cube.Transform.Position = new WorldPosition(0, 0, 20);
            cube.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, 2.0, 2.0, 2.0) });
            universe.EntityWorld.EntityManager.AddEntity(cube);
            Frame();
        }

        Drag(alt: true);
        Drag(alt: true);
        for (int i = 0; i < 10; i++)
        {
            output.Wheel(-120, new Vector2F(400, 300));
            Frame();
        }

        var position = camera.Owner.Transform.Position;
        Assert.Multiple(() =>
        {
            Assert.That(double.IsFinite(position.X) && double.IsFinite(position.Y) && double.IsFinite(position.Z), Is.True,
                $"position {position}, focus {camera.FocusDistance}, rotation {camera.Rotation}");
            Assert.That(double.IsFinite(camera.FocusDistance), Is.True);
        });
    }

    [Test]
    public void TheRightButtonAlone_StillTurnsTheCameraAboutItself()
    {
        var rotation = camera.Rotation;

        Drag(alt: false);

        Assert.Multiple(() =>
        {
            Assert.That(camera.Owner.Transform.Position, Is.EqualTo(new WorldPosition(0, 0, -10)), "it stays where it is");
            Assert.That(camera.Rotation, Is.Not.EqualTo(rotation), "and turns");
        });
    }

    // A trackpad has no right button to drag with: two fingers with Alt do it, about what is under the pointer.
    [Test]
    public void FingersPanningWithAlt_OrbitWhatIsUnderThePointer()
    {
        var face = CubeUnderThePointer();
        var before = InView(face);

        Pan(alt: true);

        var after = InView(face);
        Assert.Multiple(() =>
        {
            Assert.That((camera.Owner.Transform.Position - new WorldPosition(0, 0, -10)).Length(), Is.GreaterThan(0.1),
                "the camera went round");
            Assert.That((camera.Owner.Transform.Position - face).Length(), Is.EqualTo(29).Within(0.1), "as far from the cube");
            Assert.That(after.X, Is.EqualTo(before.X).Within(0.01));
            Assert.That(after.Y, Is.EqualTo(before.Y).Within(0.01));
        });
    }

    [Test]
    public void FingersPanningAlone_TurnTheCameraAboutItself()
    {
        var rotation = camera.Rotation;

        Pan(alt: false);

        Assert.Multiple(() =>
        {
            Assert.That(camera.Owner.Transform.Position, Is.EqualTo(new WorldPosition(0, 0, -10)), "it stays where it is");
            Assert.That(camera.Rotation, Is.Not.EqualTo(rotation), "and turns");
        });
    }

    // Alt is held before the pointer comes over what is orbited: the point is taken as the fingers move.
    [Test]
    public void ThePivotOfAPan_IsWhereThePointerIsAsItMoves_NotWhereItWasWhenAltWentDown()
    {
        var face = CubeUnderThePointer();
        output.Pointer = new Vector2F(10, 10);
        output.Key(Keys.LeftAlt, InputType.Down);
        Frame();
        Frame();
        output.Pointer = new Vector2F(400, 300);
        Frame();
        output.Gesture(new Vector2F(60, 0), 1, 0);
        Frame();

        Assert.That((camera.Owner.Transform.Position - face).Length(), Is.EqualTo(29).Within(0.1));
    }

    // The camera flown between two pans, Alt still down and the pointer where it was: the second goes about what has come
    // under it.
    [Test]
    public void APanAfterTheCameraWasMoved_TakesAPointOfItsOwn()
    {
        output.Pointer = new Vector2F(400, 300);
        output.Key(Keys.LeftAlt, InputType.Down);
        Frame();
        output.Gesture(new Vector2F(20, 0), 1, 0);
        Frame();
        camera.Owner.Transform.Position = new WorldPosition(0, 0, -10);
        camera.Rotation = QuaternionF.Identity;
        Frame();
        var target = camera.WorldPosition + camera.Forward * 30.0;
        var cube = new Entity(null, "Cube");
        cube.Transform.Position = target;
        cube.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, 2.0, 2.0, 2.0) });
        universe.EntityWorld.EntityManager.AddEntity(cube);
        Frame();
        output.Gesture(new Vector2F(60, 0), 1, 0);
        Frame();

        Assert.That((camera.Owner.Transform.Position - target).Length(), Is.EqualTo(30).Within(0.5), "about the cube");
    }

    // Alt kept down between two drags: the second goes about what the pointer is on then.
    [Test]
    public void ASecondDragWithAltStillDown_TakesAPointOfItsOwn()
    {
        output.Pointer = new Vector2F(10, 10);
        output.Key(Keys.LeftAlt, InputType.Down);
        output.Button(MouseButton.Right, InputType.Down);
        Frame();
        output.Button(MouseButton.Right, InputType.Up);
        Frame();
        var face = CubeUnderThePointer();
        output.Pointer = new Vector2F(400, 300);
        Frame();
        output.Button(MouseButton.Right, InputType.Down);
        Frame();
        output.Move(60, 0);
        Frame();

        Assert.That((camera.Owner.Transform.Position - face).Length(), Is.EqualTo(29).Within(0.1));
    }

    private WorldPosition CubeUnderThePointer()
    {
        var cube = new Entity(null, "Cube");
        cube.Transform.Position = new WorldPosition(0, 0, 20);
        cube.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, 2.0, 2.0, 2.0) });
        universe.EntityWorld.EntityManager.AddEntity(cube);
        Frame();
        return new WorldPosition(0, 0, 19);
    }

    private void Pan(bool alt)
    {
        output.Pointer = new Vector2F(400, 300);
        Frame();
        if (alt)
        {
            output.Key(Keys.LeftAlt, InputType.Down);
        }

        for (int i = 0; i < 5; i++)
        {
            output.Gesture(new Vector2F(20, 8), 1, 0);
            Frame();
        }

        if (alt)
        {
            output.Key(Keys.LeftAlt, InputType.Up);
        }

        Frame();
    }

    private void Drag(bool alt)
    {
        output.Pointer = new Vector2F(400, 300);
        Frame();
        if (alt)
        {
            output.Key(Keys.LeftAlt, InputType.Down);
        }

        output.Button(MouseButton.Right, InputType.Down);
        Frame();
        for (int i = 1; i <= 5; i++)
        {
            output.Pointer = new Vector2F(400 + 20 * i, 300 + 8 * i);
            output.Move(20, 8);
            Frame();
        }

        output.Button(MouseButton.Right, InputType.Up);
        if (alt)
        {
            output.Key(Keys.LeftAlt, InputType.Up);
        }

        Frame();
    }

    private Vector3F InView(WorldPosition point) =>
        Vector3F.TransformNormal((point - camera.Owner.Transform.Position).As<float>(),
            Matrix4x4.CreateFromQuaternion<float>(camera.Rotation));

    private void Frame()
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        output.Frame();
        universe.Satellites.Get<InputActions>().Update(output.Input, output.Input);
        input.Update(new AppTime { FrameTime = 1.0 / 60 });
        camera.Update(new AppTime { FrameTime = 1.0 / 60 });
        transforms.Update(new AppTime());
    }
}
