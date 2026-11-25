using System;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Graphics.Core.Vertices;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>Binding vertex buffers allocates nothing, one or several.</summary>
[TestFixture]
public class VertexBufferAllocationTests
{
    private const int Calls = 1000;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void BindingVertexBuffers_AllocatesNothing()
    {
        var device = GpuFixture.CreateRenderDevice();
        var prms = new PresentationParameters(PresenterType.RenderTarget, 64, 64, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, prms, "vertex_buffers");
        using var first = Adamantium.Graphics.Buffer.Vertex.New(device, new UIVertex[3]);
        using var second = Adamantium.Graphics.Buffer.Vertex.New(device, new UIVertex[3]);
        IBuffer[] both = [first, second];
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.Presenter = presenter;
        Assert.That(device.BeginDraw(), Is.True);
        device.VertexType = typeof(UIVertex);

        var one = AllocatedBy(() => device.SetVertexBuffer(first));
        var listed = AllocatedBy(() => device.SetVertexBuffers(first, second));
        var array = AllocatedBy(() => device.SetVertexBuffers(both));

        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        Assert.Multiple(() =>
        {
            Assert.That(one, Is.Zero, "SetVertexBuffer");
            Assert.That(listed, Is.Zero, "SetVertexBuffers, buffers listed");
            Assert.That(array, Is.Zero, "SetVertexBuffers, an array");
        });
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
