using System.Collections.Generic;
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
using NUnit.Framework;

namespace Adamantium.EngineTests;

/// <summary>A camera of the scene that an output looks through, steered by the mouse: one edit from the press to the
/// release, told once the camera has been still a moment, that puts the camera back either way.</summary>
[TestFixture]
public class CameraSteeringTests
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
        var cameraEntity = new CameraTemplate().BuildEntity(null, "Scene camera", Vector3<double>.Zero,
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
    public void APan_IsOneEdit_ToldWhenItIsOver()
    {
        List<EntityEdit> edits = [];
        input.CameraSteered += (_, e) => edits.Add(e.Edit);
        var start = camera.Owner.Transform.Position;
        output.Pointer = new Vector2F(400, 300);
        Frame();
        output.Button(MouseButton.Middle, InputType.Down);
        Frame();
        output.Pointer = new Vector2F(460, 320);
        Frame();
        output.Pointer = new Vector2F(520, 340);
        Frame();
        var toldWhilePanning = edits.Count;
        output.Button(MouseButton.Middle, InputType.Up);
        for (int i = 0; i < 30; i++)
        {
            Frame();
        }

        var moved = camera.Owner.Transform.Position;
        edits[0].ApplyBefore();
        var back = camera.Owner.Transform.Position;

        Assert.Multiple(() =>
        {
            Assert.That((moved - start).Length(), Is.GreaterThan(1e-6), "the pan moves the camera");
            Assert.That(toldWhilePanning, Is.Zero, "nothing while panning");
            Assert.That(edits, Has.Count.EqualTo(1), "one edit for the whole pan");
            Assert.That(back, Is.EqualTo(start));
        });
    }

    private void Frame()
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        output.Frame();
        input.Update(new AppTime { FrameTime = 1.0 / 60 });
        camera.Update(new AppTime { FrameTime = 1.0 / 60 });
        transforms.Update(new AppTime());
    }
}
