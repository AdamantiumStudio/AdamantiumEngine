using System;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>
/// A retired resource waits for the frames in flight of every render device. A device that is not drawing - an
/// off-screen one between two bakes - has nothing in flight, so it must not hold the resource back: waiting for it to
/// begin a frame it may never begin kept every retired resource of the application, and all they referenced, for good.
/// </summary>
[TestFixture]
public class RetireIdleDeviceTests
{
    private sealed class Retired : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void AResource_IsNotHeldBack_ByDevicesThatAreNotDrawing()
    {
        GpuFixture.CreateRenderDevice();
        GpuFixture.CreateRenderDevice();
        var resource = new Retired();

        GpuFixture.Main.RetireResource(resource);
        GpuFixture.Main.OnFrameStarted();

        Assert.That(resource.Disposed, Is.True);
    }

    // A device disposed by whoever holds it - a universe output going with its universe - destroys its fences. Left in
    // the registry, the next frame start polled them and took the process down.
    [Test]
    public void ADeviceDisposedByItsHolder_LeavesTheRegistry()
    {
        var device = GpuFixture.CreateRenderDevice();
        var resource = new Retired();
        GpuFixture.Main.RetireResource(resource);

        device.Dispose();
        GpuFixture.Main.OnFrameStarted();

        Assert.Multiple(() =>
        {
            Assert.That(GpuFixture.Main.GraphicsDevices, Does.Not.Contain(device));
            Assert.That(GpuFixture.Main.GetDeviceById(device.DeviceId), Is.Null);
            Assert.That(resource.Disposed, Is.True);
        });
    }

    // ...but a device in the middle of a frame may already have recorded the resource: its fences say nothing about a
    // command buffer it has not submitted yet.
    [Test]
    public void AResource_WaitsForADeviceThatIsRecordingAFrame()
    {
        var device = GpuFixture.CreateRenderDevice();
        var parameters = new PresentationParameters(PresenterType.RenderTarget, 16, 16, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, parameters, "retire_recording");
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.MSAALevel = presenter.MSAALevel;
        device.Presenter = presenter;
        Assume.That(device.BeginDraw(), Is.True, "a frame is being recorded");
        var resource = new Retired();

        GpuFixture.Main.RetireResource(resource);
        GpuFixture.Main.OnFrameStarted();
        var whileRecording = resource.Disposed;
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        Assert.That(whileRecording, Is.False);
    }
}
