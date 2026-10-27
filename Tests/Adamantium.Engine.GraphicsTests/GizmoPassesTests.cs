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

/// <summary>The instanced passes the editor draws its gizmos with: instances and a skin's joints read through pointers to
/// data uploaded for the frame, the instances after other data, so not at the start of a page.</summary>
[TestFixture]
public class GizmoPassesTests
{
    private const int Size = 100;
    private const int Grid = 10;
    private const int FloatsPerInstance = 20;

    private static readonly Vector3F[] Corners =
        [new(-1, -1, 0), new(1, -1, 0), new(-1, 1, 0), new(-1, 1, 0), new(1, -1, 0), new(1, 1, 0)];

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    // A hundred instances in one draw - more than the constant buffer's arrays held once - each where its matrix puts it,
    // in its own color.
    [Test]
    public void OneDrawOfAHundredInstances_PutsEachWhereItsMatrixSays_InItsColor()
    {
        var instances = new float[Grid * Grid * FloatsPerInstance];
        for (var i = 0; i < Grid * Grid; i++)
        {
            var world = Matrix4x4F.Scaling(0.06f) * Matrix4x4F.Translation(CellCenter(i % Grid), CellCenter(i / Grid), 0.5f);
            Write(instances, i, world, ColorOf(i));
        }

        var pixels = Draw("FlatInstanced", Quad(), instances, Grid * Grid, [], 0);

        Assert.Multiple(() =>
        {
            for (var i = 0; i < Grid * Grid; i++)
            {
                var drawn = PixelAt(pixels, CellPixel(i % Grid), CellPixel(i / Grid));
                Assert.That(drawn, Is.EqualTo(ColorOf(i)).Using<Vector4F>(Near), $"instance {i}");
            }
        });
    }

    // A skinned instance bends with its joints first: the vertices bound to joint 150 of 200 go where it carries them.
    [Test]
    public void ASkinnedInstance_BendsWithAJointFarAlongItsPalette()
    {
        var palette = new Matrix4x4F[200];
        Array.Fill(palette, Matrix4x4F.Identity);
        palette[150] = Matrix4x4F.Translation(5, 0, 0);
        var instances = new float[FloatsPerInstance];
        Write(instances, 0, Matrix4x4F.Scaling(0.1f) * Matrix4x4F.Translation(0, 0, 0.5f), new Vector4F(0, 1, 0, 1));

        var pixels = Draw("SkinnedFlatInstanced", SkinnedQuad(150), instances, 1, palette, palette.Length);

        Assert.Multiple(() =>
        {
            Assert.That(PixelAt(pixels, 75, 50), Is.EqualTo(new Vector4F(0, 1, 0, 1)).Using<Vector4F>(Near),
                "where joint 150 carried it");
            Assert.That(PixelAt(pixels, 50, 50), Is.EqualTo(new Vector4F(0, 0, 0, 1)).Using<Vector4F>(Near),
                "where it stood unbent");
        });
    }

    private static MeshVertex[] Quad()
    {
        var quad = new MeshVertex[Corners.Length];
        for (var i = 0; i < Corners.Length; i++)
        {
            quad[i].Position = Corners[i];
        }

        return quad;
    }

    private static SkinnedMeshVertex[] SkinnedQuad(int joint)
    {
        var quad = new SkinnedMeshVertex[Corners.Length];
        for (var i = 0; i < Corners.Length; i++)
        {
            quad[i].Position = Corners[i];
            quad[i].JointIndices = new Vector4F(joint, 0, 0, 0);
            quad[i].JointWeights = Vector4F.UnitX;
        }

        return quad;
    }

    private static float CellCenter(int cell) => (cell + 0.5f) / Grid * 2 - 1;

    private static int CellPixel(int cell) => (int)((cell + 0.5f) / Grid * Size);

    private static Vector4F ColorOf(int instance) =>
        new((instance % Grid * 25 + 10) / 255f, (instance / Grid * 25 + 10) / 255f, 0.5f, 1);

    private static void Write(float[] instances, int index, Matrix4x4F world, Vector4F color)
    {
        var at = instances.AsSpan(index * FloatsPerInstance, FloatsPerInstance);
        MemoryMarshal.Write(MemoryMarshal.AsBytes(at[..16]), in world);
        MemoryMarshal.Write(MemoryMarshal.AsBytes(at[16..]), in color);
    }

    private static byte[] Draw<TVertex>(string pass, TVertex[] quad, float[] instances, uint instanceCount,
        Matrix4x4F[] palette, int joints) where TVertex : struct
    {
        var device = GpuFixture.CreateRenderDevice();
        var gd = (GraphicsDevice)device;
        using var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "BasicEffect.fx"), device);
        var parameters = effect.Parameters;
        parameters["viewProjection"].SetValue(Matrix4x4F.Identity);
        parameters["view"].SetValue(Matrix4x4F.Identity);
        parameters["JointCount"].SetValue((uint)joints);

        using var vertices = Adamantium.Graphics.Buffer.Vertex.New(device, quad);
        var presentation = new PresentationParameters(PresenterType.RenderTarget, Size, Size, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, presentation, "gizmo_passes");
        var target = (Texture)presenter.RenderTarget.ResolveTexture;
        device.ClearColor = Colors.Black;
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.MSAALevel = presenter.MSAALevel;
        device.Presenter = presenter;
        device.CullMode = CullModeFlagBits.None;
        gd.ColorBlendEnabled = false;

        Assert.That(device.BeginDraw(), Is.True);
        device.SetViewports(new Viewport { Width = Size, Height = Size, MinDepth = 0, MaxDepth = 1 });
        device.SetScissors(new Rect2D { Extent = new Extent2D { Width = Size, Height = Size } });
        parameters["Joints"].SetValue(device.UploadFrameData<Matrix4x4F>(palette));
        parameters["Instances"].SetValue(device.UploadFrameData<float>(instances));
        device.VertexType = typeof(TVertex);
        device.PrimitiveTopology = PrimitiveTopology.TriangleList;
        effect.Techniques["Basic"].Passes[pass].Apply();
        gd.Draw(vertices, (uint)quad.Length, instanceCount);
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        using var image = target.ReadbackToImage();
        var pixels = new byte[Size * Size * 4];
        Marshal.Copy(image.DataPointer, pixels, 0, pixels.Length);
        return pixels;
    }

    private static Vector4F PixelAt(byte[] pixels, int x, int y)
    {
        var at = (y * Size + x) * 4;
        return new Vector4F(pixels[at + 2] / 255f, pixels[at + 1] / 255f, pixels[at] / 255f, pixels[at + 3] / 255f);
    }

    private static int Near(Vector4F a, Vector4F b) => (a - b).Length() < 0.02f ? 0 : 1;
}
