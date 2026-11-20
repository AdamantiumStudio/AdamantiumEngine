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

/// <summary>A scene a billion meters out is picked and drawn as near the origin: the scene index, the pick ray and an
/// entity hung on the camera's own all keep their places.</summary>
public class FarFromOriginViewTests
{
    private static readonly Vector3<double> Far = new(1e9, 5e8, -1e9);

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
        var cameraEntity = new CameraTemplate().BuildEntity(null, "Camera", Far, Vector3<double>.ForwardLH,
            -Vector3<double>.Up, 800, 600, 0.1f, 1000f);
        camera = cameraEntity.GetComponent<Camera>();
        output.Camera = camera;
        universe.EntityWorld.EntityManager.AddEntity(cameraEntity);
        transforms = new TransformService(universe.EntityWorld);
        transforms.Initialize();
    }

    // Two small cubes ten centimeters apart, side by side ahead: float a billion out would put them in one place.
    [Test]
    public void TwoSmallCubesSideBySide_ArePickedApart()
    {
        var ahead = (Vector3<double>)camera.Forward * 5;
        var left = Cube("Left", Far + ahead + new Vector3<double>(-0.05, 0, 0), 0.08);
        var right = Cube("Right", Far + ahead + new Vector3<double>(0.05, 0, 0), 0.08);
        universe.EntityWorld.EntityManager.AddEntity(left);
        universe.EntityWorld.EntityManager.AddEntity(right);
        Frame();

        var index = universe.EntityWorld.MainScene.Index;
        var leftHit = Picking.PickMeshes(index, PickRay.FromCamera(camera, PixelOf(left.Transform.Position)));
        var rightHit = Picking.PickMeshes(index, PickRay.FromCamera(camera, PixelOf(right.Transform.Position)));

        Assert.Multiple(() =>
        {
            Assert.That(leftHit.Entity, Is.SameAs(left));
            Assert.That(rightHit.Entity, Is.SameAs(right));
        });
    }

    // A lamp hung on the camera's own entity stands where it hangs, before the camera, for that camera too.
    [Test]
    public void AnEntityOnTheCamerasOwn_IsDrawnWhereItHangs()
    {
        var lamp = Cube("Lamp", new Vector3<double>(0.3, -0.2, 2), 0.1, camera.Owner);
        Frame();

        var drawnAt = lamp.Transform.GetMetadata(camera).WorldMatrixF.TranslationVector;
        var hangsAt = (lamp.Transform.WorldPosition - camera.WorldPosition).As<float>();

        Assert.That((drawnAt - hangsAt).Length(), Is.LessThan(1e-4f), $"drawn at {drawnAt}, hangs at {hangsAt}");
    }

    private static Entity Cube(string name, Vector3<double> at, double size, Entity owner = null)
    {
        var cube = new Entity(owner, name);
        cube.Transform.Position = at;
        cube.AddComponent(new MeshData { Mesh = Shapes.Cube.GenerateGeometry(GeometryType.Solid, size, size, size) });
        return cube;
    }

    private void Frame()
    {
        universe.EntityWorld.ForceUpdate();
        universe.Settle();
        output.Frame();
        camera.Update(new AppTime { FrameTime = 1.0 / 60 });
        transforms.Update(new AppTime());
    }

    private Vector2F PixelOf(Vector3<double> world)
    {
        var inRender = (world - camera.WorldPosition).As<float>();
        var pixel = Vector3F.Project(inRender, 0, 0, camera.Width, camera.Height, 0, 1, camera.ViewMatrix * camera.ProjectionMatrix);
        return new Vector2F(pixel.X, pixel.Y);
    }
}
