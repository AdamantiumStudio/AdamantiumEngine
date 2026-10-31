using System;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>
/// The face the engine calls the front - the one whose <c>cross(p1 - p0, p2 - p0)</c> points away from the camera - is
/// the one the GPU keeps when back faces are culled, seen through a camera as the engine's cameras see.
/// </summary>
[TestFixture]
public class FrontFaceTests
{
    private const int Size = 64;

    private struct FlatVertex
    {
        [VertexInputElement("POSITION")] public Vector2F Position;
    }

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    // In the plane z = 0, seen from z = -5 looking along +Z with the engine's up: x to the right, y down the screen.
    [TestCase(false, ExpectedResult = true)]
    [TestCase(true, ExpectedResult = false)]
    public bool CullingBackFaces_KeepsTheFaceTheEngineCallsTheFront(bool reversed)
    {
        Vector2F[] corners = reversed
            ? [new(0, 0), new(0, 1), new(1, 0)]
            : [new(0, 0), new(1, 0), new(0, 1)];
        var a = new Vector3F(corners[0], 0);
        var front = Vector3F.Cross(new Vector3F(corners[1], 0) - a, new Vector3F(corners[2], 0) - a);
        Assert.That(front.Z > 0, Is.EqualTo(!reversed), "the triangle's front points away from the camera unless reversed");

        return IsDrawn(corners);
    }

    private static bool IsDrawn(Vector2F[] corners)
    {
        var device = GpuFixture.CreateRenderDevice();
        var gd = (GraphicsDevice)device;
        var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "StrokeDraw.fx"), device);
        var view = Matrix4x4.CreateLookAtLH<float>(new Vector3F(0, 0, -5), Vector3F.Zero, Vector3F.Up);
        var projection = Matrix4x4.CreatePerspectiveFovY<float>(float.DegreesToRadians(60), 1, 0.1f, 100);
        effect.Parameters["Projection"].SetValue(view * projection);
        effect.Parameters["StrokeColor"].SetValue(new Vector4F(0, 1, 0, 1));

        var flat = new FlatVertex[corners.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            flat[i].Position = corners[i];
        }

        using var vertices = Adamantium.Graphics.Buffer.Vertex.New(device, flat);
        var parameters = new PresentationParameters(PresenterType.RenderTarget, Size, Size, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, parameters, "front_face");
        var target = (Texture)presenter.RenderTarget.ResolveTexture;
        device.ClearColor = Colors.Black;
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.MSAALevel = presenter.MSAALevel;
        device.Presenter = presenter;
        device.CullMode = CullModeFlagBits.BackBit;
        gd.ColorBlendEnabled = false;

        Assert.That(device.BeginDraw(), Is.True);
        device.SetViewports(new Viewport { Width = Size, Height = Size, MinDepth = 0, MaxDepth = 1 });
        device.SetScissors(new Rect2D { Extent = new Extent2D { Width = Size, Height = Size } });
        device.VertexType = typeof(FlatVertex);
        device.PrimitiveTopology = PrimitiveTopology.TriangleList;
        effect.Techniques[0].Passes[0].Apply();
        gd.Draw(vertices, (uint)corners.Length);
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        // The triangle covers the pixels right of and below the middle of the target; BGRA, so green is byte 1.
        using var image = target.ToImage();
        var pixel = new byte[4];
        Marshal.Copy(image.DataPointer + ((Size / 2 + 3) * Size + Size / 2 + 3) * 4, pixel, 0, 4);
        return pixel[1] > 128;
    }
}
