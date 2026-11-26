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
using engine::Adamantium.Engine.Tools;

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
    // The rows of the ground's far edge, in this view, where it fades out.
    private const int FloorFade = 16;

    private Universe _universe;
    private OffscreenOutput _output;
    private RenderingService _rendering;

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
        rendering.AttachProcessor(new ForwardPlusRenderingProcessor());
        _rendering = rendering;
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
        _universe.EntityWorld.MainScene.Environment.HighDynamicRange = highDynamicRange;
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

    // A cube on a floor in the sun, which leans towards -Z: with the sun's shadows on, the floor just past the cube on that
    // side is dark and the floor on the other side lit; without, both are lit alike.
    [TestCase(false)]
    [TestCase(true)]
    public void ACubeOnAFloor_CastsItsShadow_OnlyWhenTheSunHasShadows(bool shadows)
    {
        var eye = _output.Camera;
        eye.Owner.Transform.Position = new WorldPosition(0, -12, -16);
        eye.SetFreeLookAt(WorldPosition.Zero);
        var scene = _universe.EntityWorld.MainScene;
        var lights = new LightManager(_universe, scene);
        scene.Satellites.Add(lights);
        var sun = new LightTemplate().BuildEntity(null, "Sun", LightType.Directional);
        sun.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitX, float.DegreesToRadians(150));
        sun.GetComponent<Light>().IsWithShadows = shadows;
        lights.AddLight(sun);
        var cube = new ShapeTemplate(new CubeShape()).Build(new Entity(null, "Cube"));
        cube.AddComponent(new Material());
        _universe.EntityWorld.EntityManager.AddEntity(cube, scene);
        // Up is -Y: the floor's top meets the cube's bottom.
        var floor = new ShapeTemplate(new CubeShape()).Build(new Entity(null, "Floor"));
        floor.Transform.Position = new WorldPosition(0, 0.55, 0);
        floor.Transform.ScaleFactor = new Vector3F(8, 0.1f, 8);
        floor.AddComponent(new Material());
        _universe.EntityWorld.EntityManager.AddEntity(floor, scene);
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        var pixels = Pixels(_output.ReadFrame(), $"forward-plus-shadow-{shadows}.png");
        var camera = _output.Camera;
        int BrightnessAt(WorldPosition point)
        {
            var start = PixelStart(camera, point);
            return (pixels[start] + pixels[start + 1] + pixels[start + 2]) / 3;
        }

        // The sun 30 degrees off straight down: the shadow of the cube, 1 high, reaches 0.58 past its -Z face.
        var inShadow = BrightnessAt(new WorldPosition(0, 0.5, -0.8));
        var sideways = BrightnessAt(new WorldPosition(1.6, 0.5, -0.8));

        Assert.Multiple(() =>
        {
            Assert.That(sideways, Is.GreaterThan(120), "the floor away from the cube, in the sun");
            if (shadows)
            {
                Assert.That(inShadow, Is.LessThan(sideways / 3), "the floor in the cube's shadow");
            }
            else
            {
                Assert.That(Math.Abs(inShadow - sideways), Is.LessThanOrEqualTo(EvenWithin), "no shadow without shadows");
            }
        });
    }

    // A cube hung 10 above the floor, behind and above a camera looking down at the floor: out of view, its shadow still
    // falls where the camera looks.
    [Test]
    public void ACubeOutOfView_StillCastsItsShadowIntoIt()
    {
        var eye = _output.Camera;
        eye.Owner.Transform.Position = new WorldPosition(0, -3, -8);
        eye.SetFreeLookAt(new WorldPosition(0, 0.5, -6.3));
        var scene = _universe.EntityWorld.MainScene;
        var lights = new LightManager(_universe, scene);
        scene.Satellites.Add(lights);
        var sun = new LightTemplate().BuildEntity(null, "Sun", LightType.Directional);
        sun.Transform.Rotation = Quaternion.CreateFromAxisAngle<float>(Vector3F.UnitX, float.DegreesToRadians(150));
        sun.GetComponent<Light>().IsWithShadows = true;
        lights.AddLight(sun);
        var cube = new ShapeTemplate(new CubeShape()).Build(new Entity(null, "Cube"));
        cube.Transform.Position = new WorldPosition(0, -10, 0);
        cube.AddComponent(new Material());
        _universe.EntityWorld.EntityManager.AddEntity(cube, scene);
        var floor = new ShapeTemplate(new CubeShape()).Build(new Entity(null, "Floor"));
        floor.Transform.Position = new WorldPosition(0, 0.55, 0);
        floor.Transform.ScaleFactor = new Vector3F(20, 0.1f, 20);
        floor.AddComponent(new Material());
        _universe.EntityWorld.EntityManager.AddEntity(floor, scene);
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        var pixels = Pixels(_output.ReadFrame(), "forward-plus-shadow-out-of-view.png");
        var camera = _output.Camera;
        int BrightnessAt(WorldPosition point)
        {
            var start = PixelStart(camera, point);
            return (pixels[start] + pixels[start + 1] + pixels[start + 2]) / 3;
        }

        // 10 to 11 above the floor, the sun 30 degrees off straight down: the shadow lies 5.3 to 6.9 towards -Z.
        var inShadow = BrightnessAt(new WorldPosition(0, 0.5, -6.1));
        var sideways = BrightnessAt(new WorldPosition(1.6, 0.5, -6.1));

        Assert.Multiple(() =>
        {
            Assert.That(sideways, Is.GreaterThan(120), "the floor beside the shadow, in the sun");
            Assert.That(inShadow, Is.LessThan(sideways / 3), "the floor in the shadow of the cube out of view");
        });
    }

    // The furnace: under the same light from every side and no other, a white surface sends back what it gets - neither
    // brighter nor darker than the light - whether it is a rough paint or a metal.
    [TestCase(0f, 1f)]
    [TestCase(1f, 0.5f)]
    public void AWhiteCube_UnderAnEvenSky_SendsBackTheLightItGets(float metal, float rough)
    {
        var eye = _output.Camera;
        eye.Owner.Transform.Position = new WorldPosition(0, 0, -4);
        eye.SetFreeLookAt(WorldPosition.Zero);
        var scene = _universe.EntityWorld.MainScene;
        scene.Environment.Sky = SkyKind.Even;
        scene.Environment.SkyStrength = 0.8f;
        var cube = new ShapeTemplate(new CubeShape()).Build(new Entity(null, "Cube"));
        cube.AddComponent(new Material
        {
            Lighting = LightingModel.Physical, DiffuseColor = Vector4F.One, MetallicFactor = metal, RoughnessFactor = rough
        });
        _universe.EntityWorld.EntityManager.AddEntity(cube, scene);
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        var pixels = Pixels(_output.ReadFrame(), $"forward-plus-furnace-{metal}-{rough}.png");
        var start = PixelStart(_output.Camera, new WorldPosition(0, 0, -0.5));
        var face = (pixels[start] + pixels[start + 1] + pixels[start + 2]) / 3;
        // The sky's 0.8 as the screen shows it.
        var expected = (int)(255 * (1.055 * Math.Pow(0.8, 1 / 2.4) - 0.055));

        Assert.That(face, Is.InRange(expected - 12, expected + 12), $"the face sends back the sky's {expected}");
    }

    // A studio sky behind a scene with nothing in it: light overhead, darker below.
    [Test]
    public void AStudioSky_IsDrawnBehindTheScene_LightAboveAndDarkBelow()
    {
        var eye = _output.Camera;
        eye.Owner.Transform.Position = WorldPosition.Zero;
        eye.SetFreeLookAt(new WorldPosition(0, 0, 10));
        var scene = _universe.EntityWorld.MainScene;
        scene.Environment.Sky = SkyKind.Studio;
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        var pixels = Pixels(_output.ReadFrame(), "forward-plus-studio-sky.png");
        int BrightnessAt(int x, int y)
        {
            var start = (y * (int)Size + x) * 4;
            return (pixels[start] + pixels[start + 1] + pixels[start + 2]) / 3;
        }

        var top = BrightnessAt((int)Size / 2, 2);
        var bottom = BrightnessAt((int)Size / 2, (int)Size - 3);

        Assert.Multiple(() =>
        {
            Assert.That(top, Is.GreaterThan(150), "the sky above");
            Assert.That(bottom, Is.LessThan(top - 40), "the floor below, darker");
        });
    }

    // The editor's ground grid lies over the sky: below the horizon its floor all along, no speck of the sky through it
    // where the floor runs past the far plane - in a high dynamic range frame as in a plain one.
    [TestCase(false)]
    [TestCase(true)]
    public void TheGroundGrid_LiesOverTheSky_WithNoSpeckAtTheHorizon(bool highDynamicRange)
    {
        var eye = _output.Camera;
        // So near that the far floor's depth, pushed back a little, comes to the far end itself - where nothing was drawn.
        eye.ZNear = 0.0005f;
        eye.Owner.Transform.Position = new WorldPosition(0, -2, 0);
        eye.SetFreeLookAt(new WorldPosition(0, -1.6, 10));
        var scene = _universe.EntityWorld.MainScene;
        scene.Environment.HighDynamicRange = highDynamicRange;
        _universe.Satellites.Add(new engine::Adamantium.Engine.Observatory(_universe, _universe.EntityWorld));
        var tools = _universe.EntityWorld.CreateService<ToolsService>(_universe.EntityWorld);
        tools.AttachProcessor(new GroundGrid());
        _rendering.AttachProcessor(new ToolsOverlayProcessor(tools));
        byte[] Frame(SkyKind sky)
        {
            scene.Environment.Sky = sky;
            for (var frame = 0; frame < Frames; frame++)
            {
                RunFrame();
            }

            return Pixels(_output.ReadFrame(), $"forward-plus-sky-under-grid-{sky}-{highDynamicRange}.png");
        }

        var bare = Frame(SkyKind.None);
        var starry = Frame(SkyKind.Stars);
        // Wherever the floor shows without a sky, it shows the same with one - but along its far edge, where it fades out
        // and what is behind shows through it, as it should.
        bool IsFloor(int pixel) => bare[pixel] + bare[pixel + 1] + bare[pixel + 2] >= 30;
        var farEdge = 0;
        while (farEdge < (int)Size && !IsFloor((farEdge * (int)Size + (int)Size / 2) * 4))
        {
            farEdge++;
        }

        var floorPixels = 0;
        var differing = 0;
        for (var pixel = (farEdge + FloorFade) * (int)Size * 4; pixel < bare.Length; pixel += 4)
        {
            if (!IsFloor(pixel))
            {
                continue;
            }

            floorPixels++;
            if (Math.Abs(bare[pixel] - starry[pixel]) > 6 || Math.Abs(bare[pixel + 2] - starry[pixel + 2]) > 6)
            {
                differing++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(floorPixels, Is.GreaterThan(Size * Size / 8), "the floor fills the lower part");
            Assert.That(differing, Is.EqualTo(0), $"floor pixels the sky shows through, of {floorPixels}");
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
