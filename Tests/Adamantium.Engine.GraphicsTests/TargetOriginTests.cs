using System;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>
/// A target that holds one region of the surface (a layer) is drawn into in the surface's coordinates: the device moves
/// what reaches the GPU, and every caller keeps reading the viewports and scissors it set.
/// </summary>
[TestFixture]
public class TargetOriginTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void ViewportsAndScissors_StayInTheSurfacesCoordinates_AndANewFrameStartsAtZero()
    {
        var device = GpuFixture.CreateRenderDevice();
        var gd = (GraphicsDevice)device;
        var prms = new PresentationParameters(PresenterType.RenderTarget, 64, 64, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, prms, "target_origin");
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.Presenter = presenter;

        Assert.That(device.BeginDraw(), Is.True);
        device.TargetOrigin = new NativePoint(16, 24);
        device.SetViewports(new Viewport { X = 0, Y = 0, Width = 64, Height = 64, MinDepth = 0, MaxDepth = 1 });
        device.SetScissors(new Rect2D
        {
            Offset = new Offset2D { X = 20, Y = 30 },
            Extent = new Extent2D { Width = 10, Height = 10 }
        });

        Assert.Multiple(() =>
        {
            Assert.That(gd.CurrentViewports[0].X, Is.EqualTo(0f));
            Assert.That(gd.CurrentScissors[0].Offset.X, Is.EqualTo(20));
            Assert.That(gd.CurrentScissors[0].Offset.Y, Is.EqualTo(30));
        });

        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        Assert.That(device.BeginDraw(), Is.True);
        Assert.That(device.TargetOrigin, Is.EqualTo(new NativePoint(0, 0)));
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();
    }
}
