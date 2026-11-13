extern alias engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using NUnit.Framework;
using engine::Adamantium.Engine.EntityServices;
using engine::Adamantium.Engine.Managers;
using engine::Adamantium.Engine.Templates;
using engine::Adamantium.Engine.Templates.Lights;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A scene drawn by the Forward+ pass into an output of its universe, as the editor draws it: the transform
/// service places it for the output's camera, the scene's light manager lights it.</summary>
[TestFixture]
public class ForwardPlusLightingTests
{
    private const uint Size = 256;
    private const int Frames = 4;
    // How far apart two pixels of one flat face may be, in 8-bit levels: the specular term moves a little with the eye.
    private const int EvenWithin = 4;

    private Universe _universe;
    private OffscreenOutput _output;

    [SetUp]
    public void MakeUniverse()
    {
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var host = new NoSurfaceHost();
        registry.RegisterInstance<IOutputFactory>(host);
        registry.RegisterInstance<IWindowingPlatform>(host);
        _universe = new Universe(UniverseMode.Slave, false, container, new SharedDeviceService());
        _universe.InitializeUniverse();
        _universe.FrameMode = FrameMode.Continuous;
        _universe.EntityWorld.CreateService<TransformService>(_universe.EntityWorld);
        _output = new OffscreenOutput(Size, Size);
        _universe.AddOutput(_output);
        RunFrame();
        var rendering = _universe.CreateRenderService<RenderingService>(_output);
        rendering.AttachProcessor(new ForwardPlusRenderingProcessor());
    }

    [TearDown]
    public void ShutDown()
    {
        _universe.ShutDown();
        GpuFixture.ReleaseRenderDevices();
    }

    // A new scene's sun - turned 150 degrees about X, shining down and towards the camera - on the cube the editor
    // creates, seen from above and in front: each face lit evenly, the top in the sun, the front, turned away from it, by
    // the ambient light alone.
    [Test]
    public void ACubeInTheSun_HasEvenlyLitFaces_TheTopLitAndTheFrontInShade()
    {
        var eye = _output.Camera;
        eye.Owner.Transform.Position = new Vector3<double>(0, -12, -16);
        eye.SetFreeLookAt(Vector3<double>.Zero);
        var scene = _universe.EntityWorld.MainScene;
        var lights = new LightManager(_universe, scene);
        var sun = new LightTemplate().BuildEntity(null, "Sun", LightType.Directional);
        sun.Transform.Position = new Vector3<double>(0, -7, 8);
        sun.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitX, float.DegreesToRadians(150));
        lights.AddLight(sun);
        var cube = new ShapeTemplate(new CubeShape()).Build(new Entity(null, "Cube"));
        cube.AddComponent(new Material());
        _universe.EntityWorld.EntityManager.AddEntity(cube, scene);
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        var pixels = Pixels(_output.ReadFrame(), "forward-plus-cube.png");
        var camera = _output.Camera;
        var top = FaceBrightness(pixels, camera, new Vector3<double>(0, -0.5, 0), Vector3<double>.UnitX,
            Vector3<double>.UnitZ);
        var front = FaceBrightness(pixels, camera, new Vector3<double>(0, 0, -0.5), Vector3<double>.UnitX,
            Vector3<double>.UnitY);

        Assert.Multiple(() =>
        {
            Assert.That(top.Max() - top.Min(), Is.LessThanOrEqualTo(EvenWithin), $"the top is flat: {string.Join(" ", top)}");
            Assert.That(front.Max() - front.Min(), Is.LessThanOrEqualTo(EvenWithin),
                $"the front is flat: {string.Join(" ", front)}");
            Assert.That(top.Average(), Is.GreaterThan(150), "the top, in the sun");
            Assert.That(front.Average(), Is.LessThan(30), "the front, turned away from the sun");
        });
    }

    // The brightness of a 4 x 4 grid of points inside a face of the unit cube.
    private static List<int> FaceBrightness(byte[] pixels, Camera camera, Vector3<double> center,
        Vector3<double> across, Vector3<double> along)
    {
        var brightness = new List<int>();
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                var point = center + across * (column * 0.2 - 0.3) + along * (row * 0.2 - 0.3);
                var start = PixelStart(camera, point);
                brightness.Add((pixels[start] + pixels[start + 1] + pixels[start + 2]) / 3);
            }
        }

        return brightness;
    }

    // Where the pixel a point of the world is drawn on starts in the picture's bytes.
    private static int PixelStart(Camera camera, Vector3<double> point)
    {
        var relative = (point - camera.WorldPosition).As<float>();
        var clip = Vector3F.TransformCoordinate(relative, camera.ViewProjectionMatrix);
        var x = Math.Clamp((int)((clip.X * 0.5f + 0.5f) * Size), 0, (int)Size - 1);
        var y = Math.Clamp((int)((clip.Y * 0.5f + 0.5f) * Size), 0, (int)Size - 1);
        return (y * (int)Size + x) * 4;
    }

    // The input service updates the editor's cameras; this universe has none.
    private void RunFrame()
    {
        _output.Camera?.Update(default);
        _universe.RunOnce(default(AppTime));
        _universe.Submit();
    }

    private static byte[] Pixels(Image picture, string savedAs)
    {
        Assert.That(picture, Is.Not.Null, "the output drew no frame");
        using var stream = new MemoryStream();
        picture.Save(stream, ImageFileType.Png);
        File.WriteAllBytes(Path.Combine(TestContext.CurrentContext.WorkDirectory, savedAs), stream.ToArray());
        stream.Position = 0;
        return BitmapLoader.Load(stream).GetRawPixels(0);
    }
}
