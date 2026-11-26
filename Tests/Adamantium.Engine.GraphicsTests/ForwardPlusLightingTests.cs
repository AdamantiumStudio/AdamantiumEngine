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
    private ForwardPlusRenderingProcessor _processor;

    [SetUp]
    public void MakeUniverse()
    {
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var host = new HeadlessPlatform();
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
        _processor = new ForwardPlusRenderingProcessor();
        rendering.AttachProcessor(_processor);
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
        eye.Owner.Transform.Position = new WorldPosition(0, -12, -16);
        eye.SetFreeLookAt(WorldPosition.Zero);
        var scene = _universe.EntityWorld.MainScene;
        var lights = new LightManager(_universe, scene);
        scene.Satellites.Add(lights);
        var sun = new LightTemplate().BuildEntity(null, "Sun", LightType.Directional);
        sun.Transform.Position = new WorldPosition(0, -7, 8);
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
        var top = FaceBrightness(pixels, camera, new WorldPosition(0, -0.5, 0), Vector3<double>.UnitX,
            Vector3<double>.UnitZ);
        var front = FaceBrightness(pixels, camera, new WorldPosition(0, 0, -0.5), Vector3<double>.UnitX,
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

    // A polished metal has no diffuse light: lit physically, its face in the sun but away from the sun's reflection is
    // darker than the same face lit simply, where the color takes the light whatever the metalness.
    [Test]
    public void AMetalCube_LitPhysically_IsDarkerThanLitSimply()
    {
        // From the side, away from where the top mirrors the sun: the sun leans towards -Z, so its reflection does too.
        var eye = _output.Camera;
        eye.Owner.Transform.Position = new WorldPosition(16, -12, 0);
        eye.SetFreeLookAt(WorldPosition.Zero);
        var scene = _universe.EntityWorld.MainScene;
        var lights = new LightManager(_universe, scene);
        scene.Satellites.Add(lights);
        var sun = new LightTemplate().BuildEntity(null, "Sun", LightType.Directional);
        sun.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitX, float.DegreesToRadians(150));
        lights.AddLight(sun);

        var simple = TopOf(LightingModel.Simple, "forward-plus-metal-simple.png");
        var physical = TopOf(LightingModel.Physical, "forward-plus-metal-physical.png");

        Assert.Multiple(() =>
        {
            Assert.That(simple, Is.GreaterThan(150), "the top lit simply, in the sun");
            Assert.That(physical, Is.LessThan(simple - 40), "the top lit physically, a metal without its reflection");
        });
    }

    // A cube giving off strong light, seen head on in the dark: in a high dynamic range frame its light spreads past its
    // edges and its face goes to white; drawn straight, nothing shows beside it.
    [TestCase(false)]
    [TestCase(true)]
    public void ABrightCube_GlowsPastItsEdges_OnlyInAHighDynamicRangeFrame(bool highDynamicRange)
    {
        _processor.HighDynamicRange = highDynamicRange;
        var eye = _output.Camera;
        eye.Owner.Transform.Position = new WorldPosition(0, 0, -4);
        eye.SetFreeLookAt(WorldPosition.Zero);
        var cube = new ShapeTemplate(new CubeShape()).Build(new Entity(null, "Cube"));
        cube.AddComponent(new Material { DiffuseColor = new Vector4F(0, 0, 0, 1), Emission = new Vector4F(8, 4, 2, 0) });
        _universe.EntityWorld.EntityManager.AddEntity(cube, _universe.EntityWorld.MainScene);
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        var pixels = Pixels(_output.ReadFrame(), $"forward-plus-glow-{highDynamicRange}.png");
        var camera = _output.Camera;
        var face = PixelStart(camera, new WorldPosition(0, 0, -0.5));
        var beside = PixelStart(camera, new WorldPosition(0.75, 0, -0.5));
        var besideBrightness = (pixels[beside] + pixels[beside + 1] + pixels[beside + 2]) / 3;

        Assert.Multiple(() =>
        {
            Assert.That(pixels[face], Is.GreaterThan(240), "the face's red, its brightest");
            if (highDynamicRange)
            {
                Assert.That(pixels[face + 2], Is.GreaterThan(150), "the face's blue: strong light goes to white");
                Assert.That(besideBrightness, Is.GreaterThan(8), "the glow beside the cube");
            }
            else
            {
                Assert.That(besideBrightness, Is.LessThan(3), "nothing beside the cube");
            }
        });
    }

    // How bright the top of a metal cube in the sun is, lit by the given model.
    private double TopOf(LightingModel lighting, string savedAs)
    {
        var scene = _universe.EntityWorld.MainScene;
        var cube = new ShapeTemplate(new CubeShape()).Build(new Entity(null, "Cube"));
        cube.AddComponent(new Material { Lighting = lighting, MetallicFactor = 1, RoughnessFactor = 0.3f });
        _universe.EntityWorld.EntityManager.AddEntity(cube, scene);
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        var top = FaceBrightness(Pixels(_output.ReadFrame(), savedAs), _output.Camera, new WorldPosition(0, -0.5, 0),
            Vector3<double>.UnitX, Vector3<double>.UnitZ);
        _universe.EntityWorld.EntityManager.RemoveEntity(cube);
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        return top.Average();
    }

    // The brightness of a 4 x 4 grid of points inside a face of the unit cube.
    private static List<int> FaceBrightness(byte[] pixels, Camera camera, WorldPosition center,
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
    private static int PixelStart(Camera camera, WorldPosition point)
    {
        var relative = point.OffsetFrom(camera.WorldPosition);
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
