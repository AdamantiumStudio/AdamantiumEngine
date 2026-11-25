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

/// <summary>Several vertex buffers drawn together, each of a vertex structure of its own; binding them allocates
/// nothing.</summary>
[TestFixture]
public class VertexBufferTests
{
    private const int Calls = 1000;
    private const int Size = 64;

    private struct Corner
    {
        [VertexInputElement("POSITION")] public Vector2F Position;
    }

    [PerInstanceData]
    private struct Instance
    {
        [VertexInputElement("TEXCOORD0")] public Vector2F Offset;
        [VertexInputElement("COLOR0")] public Vector4F Color;
    }

    private IGraphicsDevice _device;
    private GraphicsPresenter _presenter;

    [SetUp]
    public void MakeTarget()
    {
        _device = GpuFixture.CreateRenderDevice();
        _presenter = GraphicsPresenter.Create(_device,
            new PresentationParameters(PresenterType.RenderTarget, Size, Size, IntPtr.Zero), "vertex_buffers");
        _device.ClearColor = Colors.Black;
        _device.SetRenderTargets(_presenter.RenderTarget);
        _device.SetDepthBuffer(_presenter.DepthBuffer);
        _device.MSAALevel = _presenter.MSAALevel;
        _device.Presenter = _presenter;
        _device.CullMode = CullModeFlagBits.None;
        ((GraphicsDevice)_device).ColorBlendEnabled = false;
    }

    [TearDown]
    public void ReleaseDevices()
    {
        _presenter.Dispose();
        GpuFixture.ReleaseRenderDevices();
    }

    // The buffers are bound before the types are known too: the strides follow the types.
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void TwoInstances_TakeTheirCornersFromOneBuffer_AndTheirPlaceAndColorFromTheOther(bool isIndexed,
        bool areTypesSetLast)
    {
        var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "InstancedDraw.fx"), _device);
        using var corners = Adamantium.Graphics.Buffer.Vertex.New(_device, new Corner[]
        {
            new() { Position = new Vector2F(-0.5f, -1) }, new() { Position = new Vector2F(0.5f, -1) },
            new() { Position = new Vector2F(-0.5f, 1) }, new() { Position = new Vector2F(0.5f, 1) }
        });
        using var instances = Adamantium.Graphics.Buffer.Vertex.New(_device, new Instance[]
        {
            new() { Offset = new Vector2F(-0.5f, 0), Color = new Vector4F(0, 1, 0, 1) },
            new() { Offset = new Vector2F(0.5f, 0), Color = new Vector4F(0, 0, 1, 1) }
        });
        using var indices = Adamantium.Graphics.Buffer.Index.New(_device, new uint[] { 0, 1, 2, 2, 1, 3 });

        Assert.That(_device.BeginDraw(), Is.True);
        _device.SetViewports(new Viewport { Width = Size, Height = Size, MinDepth = 0, MaxDepth = 1 });
        _device.SetScissors(new Rect2D { Extent = new Extent2D { Width = Size, Height = Size } });
        _device.PrimitiveTopology = isIndexed ? PrimitiveTopology.TriangleList : PrimitiveTopology.TriangleStrip;
        if (!areTypesSetLast)
        {
            _device.SetVertexTypes(typeof(Corner), typeof(Instance));
        }

        _device.SetVertexBuffers(corners, instances);
        if (areTypesSetLast)
        {
            _device.SetVertexTypes(typeof(Corner), typeof(Instance));
        }

        effect.Techniques[0].Passes[0].Apply();
        if (isIndexed)
        {
            _device.SetIndexBuffer(indices);
            _device.DrawIndexed(6u, 2);
        }
        else
        {
            _device.Draw(4, 2);
        }

        Finish();
        _device.DeviceWaitIdle();

        using var image = ((Texture)_presenter.RenderTarget.ResolveTexture).ToImage();
        Assert.Multiple(() =>
        {
            Assert.That(PixelOf(image, Size / 4), Is.EqualTo(new byte[] { 0, 255, 0, 255 }), "the first instance, BGRA");
            Assert.That(PixelOf(image, Size * 3 / 4), Is.EqualTo(new byte[] { 255, 0, 0, 255 }), "the second, BGRA");
        });
    }

    [Test]
    public void TheTypes_AreTheOnesGiven_AndTheVertexTypeTheFirst()
    {
        _device.SetVertexTypes(typeof(Corner), typeof(Instance));
        var types = _device.VertexTypes.ToArray();
        var first = _device.VertexType;
        _device.VertexType = typeof(UIVertex);
        var one = _device.VertexTypes.ToArray();
        _device.VertexType = null;

        Assert.Multiple(() =>
        {
            Assert.That(types, Is.EqualTo(new[] { typeof(Corner), typeof(Instance) }));
            Assert.That(first, Is.EqualTo(typeof(Corner)));
            Assert.That(one, Is.EqualTo(new[] { typeof(UIVertex) }));
            Assert.That(_device.VertexType, Is.Null);
            Assert.That(_device.VertexTypes.Length, Is.Zero);
        });
    }

    [Test]
    public void SettingTypes_AndBindingBuffers_AllocatesNothing()
    {
        using var first = Adamantium.Graphics.Buffer.Vertex.New(_device, new UIVertex[3]);
        using var second = Adamantium.Graphics.Buffer.Vertex.New(_device, new UIVertex[3]);
        IBuffer[] both = [first, second];
        Assert.That(_device.BeginDraw(), Is.True);

        var types = AllocatedBy(() =>
        {
            _device.VertexType = typeof(UIVertex);
            _device.SetVertexTypes(typeof(Corner), typeof(Instance));
        });
        var one = AllocatedBy(() => _device.SetVertexBuffer(first));
        var listed = AllocatedBy(() => _device.SetVertexBuffers(first, second));
        var array = AllocatedBy(() => _device.SetVertexBuffers(both));
        Finish();
        _device.DeviceWaitIdle();

        Assert.Multiple(() =>
        {
            Assert.That(types, Is.Zero, "SetVertexTypes");
            Assert.That(one, Is.Zero, "SetVertexBuffer");
            Assert.That(listed, Is.Zero, "SetVertexBuffers, buffers listed");
            Assert.That(array, Is.Zero, "SetVertexBuffers, an array");
        });
    }

    private void Finish()
    {
        _device.EndDraw();
        _device.Submit();
        _presenter.Present();
        _device.FrameEnded();
    }

    private static byte[] PixelOf(Adamantium.Imaging.Image image, int x)
    {
        var pixel = new byte[4];
        Marshal.Copy(image.DataPointer + (Size / 2 * Size + x) * 4, pixel, 0, 4);
        return pixel;
    }

    private static long AllocatedBy(Action bind)
    {
        bind();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < Calls; i++)
        {
            bind();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
