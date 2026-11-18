extern alias engine;
using System;
using System.IO;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using NUnit.Framework;
using engine::Adamantium.Engine.EntityServices;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>The wireframe the Forward+ pass draws, alone or over the shading, of a mesh that bends with its joints: it
/// goes where the joints carry the mesh, as the shading does.</summary>
[TestFixture]
public class ForwardPlusWireframeTests
{
    private const uint Size = 256;
    private const int Frames = 4;
    // A pixel brighter than this, its three channels summed, is the wire's: the background is black and a surface lit by
    // the ambient light alone is far darker.
    private const int WireBrightness = 200;
    private const double Carried = 3;

    private Universe _universe;
    private OffscreenOutput _output;
    private ForwardPlusRenderingProcessor _processor;

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
        _processor = new ForwardPlusRenderingProcessor();
        rendering.AttachProcessor(_processor);
    }

    [TearDown]
    public void ShutDown()
    {
        _universe.ShutDown();
        GpuFixture.ReleaseRenderDevices();
    }

    // The arms of a robot and whole characters stayed shaded in a wireframe view: the wire was drawn by passes that do not
    // bend a mesh, so a skinned one was left out of it.
    [TestCase(ViewDisplay.Wireframe)]
    [TestCase(ViewDisplay.ShadedWireframe)]
    public void ASkinnedMesh_HasItsWire_WhereItsJointCarriedIt(ViewDisplay display)
    {
        _processor.Display = display;
        var eye = _output.Camera;
        eye.Owner.Transform.Position = new Vector3<double>(0, 0, -10);
        eye.SetFreeLookAt(Vector3<double>.Zero);
        AddCarriedTriangle();
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        var pixels = Pixels(_output.ReadFrame(), $"skinned-wire-{display}.png");
        var camera = _output.Camera;
        var whereCarried = WirePixels(pixels, camera, new Vector3<double>(Carried, 0, 0));
        var whereBound = WirePixels(pixels, camera, Vector3<double>.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(whereCarried, Is.GreaterThan(20), "the wire, where the joint carried the mesh");
            Assert.That(whereBound, Is.Zero, "no wire where the mesh stood unbent");
        });
    }

    // A shape given no collider vanished: the pass skipped any entity without one. Colliders are for the physics only.
    [Test]
    public void AMeshWithNoCollider_IsDrawn()
    {
        // Shaded: a wire in the shading needs barycentrics, which not every GPU has.
        _processor.Display = ViewDisplay.Shaded;
        var eye = _output.Camera;
        eye.Owner.Transform.Position = new Vector3<double>(0, 0, -10);
        eye.SetFreeLookAt(Vector3<double>.Zero);
        var plain = new Entity(null, "Plain");
        var mesh = new Mesh(PrimitiveType.TriangleList);
        mesh.SetPoints([new Vector3<double>(-1, 1, 0), new Vector3<double>(1, 1, 0), new Vector3<double>(0, -1, 0)])
            .SetIndices([0, 1, 2]);
        plain.AddComponent(new MeshData { Mesh = mesh });
        plain.AddComponent(new Material());
        _universe.EntityWorld.EntityManager.AddEntity(plain, _universe.EntityWorld.MainScene);
        for (var frame = 0; frame < Frames; frame++)
        {
            RunFrame();
        }

        var pixels = Pixels(_output.ReadFrame(), "no-collider.png");
        var lit = 0;
        for (var start = 0; start + 2 < pixels.Length; start += 4)
        {
            if (pixels[start] + pixels[start + 1] + pixels[start + 2] > 0)
                lit++;
        }

        Assert.Multiple(() =>
        {
            Assert.That(_processor.TrianglesDrawn, Is.EqualTo(1), "the triangle, counted as drawn");
            Assert.That(lit, Is.GreaterThan(100), "its pixels on the black background");
        });
    }

    // A triangle bound whole to one joint, the joint then moved sideways.
    private void AddCarriedTriangle()
    {
        var skinned = new Entity(null, "Skinned");
        var mesh = new Mesh(PrimitiveType.TriangleList);
        mesh.SetPoints([new Vector3<double>(-1, 1, 0), new Vector3<double>(1, 1, 0), new Vector3<double>(0, -1, 0)])
            .SetIndices([0, 1, 2]);
        mesh.SetJointIndices([Vector4F.Zero, Vector4F.Zero, Vector4F.Zero]);
        mesh.SetJointWeights([Vector4F.UnitX, Vector4F.UnitX, Vector4F.UnitX]);
        skinned.AddComponent(new MeshData { Mesh = mesh, RenderMode = MeshRenderMode.Skinned });
        skinned.AddComponent(new Material());
        skinned.AddComponent(new BoxCollider());
        var joint = new Entity(skinned, "Joint");
        var skin = new Skin();
        skin.Joints.Add(joint);
        skin.InverseBindMatrices.Add(Matrix4x4F.Identity);
        skinned.AddComponent(skin);
        _universe.EntityWorld.EntityManager.AddEntity(skinned, _universe.EntityWorld.MainScene);
        joint.Transform.Position = new Vector3<double>(Carried, 0, 0);
    }

    // The wire's pixels in the square the triangle stands in, centered on center.
    private static int WirePixels(byte[] pixels, Camera camera, Vector3<double> center)
    {
        var low = PixelOf(camera, center + new Vector3<double>(-1.2, -1.2, 0));
        var high = PixelOf(camera, center + new Vector3<double>(1.2, 1.2, 0));
        var count = 0;
        for (var y = Math.Min(low.Y, high.Y); y <= Math.Max(low.Y, high.Y); y++)
        {
            for (var x = Math.Min(low.X, high.X); x <= Math.Max(low.X, high.X); x++)
            {
                var start = (y * (int)Size + x) * 4;
                if (pixels[start] + pixels[start + 1] + pixels[start + 2] > WireBrightness)
                    count++;
            }
        }

        return count;
    }

    // The pixel a point of the world is drawn on.
    private static Vector2<int> PixelOf(Camera camera, Vector3<double> point)
    {
        var relative = (point - camera.WorldPosition).As<float>();
        var clip = Vector3F.TransformCoordinate(relative, camera.ViewProjectionMatrix);
        var x = Math.Clamp((int)((clip.X * 0.5f + 0.5f) * Size), 0, (int)Size - 1);
        var y = Math.Clamp((int)((clip.Y * 0.5f + 0.5f) * Size), 0, (int)Size - 1);
        return new Vector2<int>(x, y);
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
