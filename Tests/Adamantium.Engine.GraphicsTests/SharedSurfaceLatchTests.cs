using System;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>
/// The consumer side of a shared surface: a frame is latched once, however many draws sample it. Latched twice, it was
/// signaled on the consume timeline twice with the same value, which Vulkan forbids.
/// </summary>
[TestFixture]
public class SharedSurfaceLatchTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void ANewFrame_IsHandedOutOnce_WhoeverAsks()
    {
        var device = (GraphicsDevice)CreateSharingDevice();
        using var surface = SharedSurface.CreateExportable(device, 4, 4, SurfaceFormat.B8G8R8A8.UNorm);

        Assert.That(surface.LatchNewFrame(), Is.Zero, "nothing produced yet");

        Produce(device, surface, 1);
        Assert.That(surface.LatchNewFrame(), Is.EqualTo(1));
        Assert.That(surface.LatchNewFrame(), Is.Zero, "the same frame, asked for again");

        Produce(device, surface, 2);
        Assert.That(surface.LatchNewFrame(), Is.EqualTo(2));
    }

    // The consumer sampled the surface itself, so a frame drawn again while nothing new came raced the producer's next
    // write. It samples a copy now, made once per new frame, and the consume timeline moves only once that copy is done.
    [Test]
    public void ANewFrame_IsCopiedOnce_AndReleasedToTheProducerOnlyByTheCopy()
    {
        var device = CreateSharingDevice();
        var gd = (GraphicsDevice)device;
        var prms = new PresentationParameters(PresenterType.RenderTarget, 4, 4, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, prms, "shared_surface_latch");
        using var surface = SharedSurface.CreateExportable(device, 4, 4, SurfaceFormat.B8G8R8A8.UNorm);
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.Presenter = presenter;

        Frame(device, presenter, surface);
        Assert.That(surface.LatchedCopy, Is.Null, "nothing produced yet");

        Produce(gd, surface, 1);
        Frame(device, presenter, surface);
        var copy = surface.LatchedCopy;
        Assert.That(copy, Is.Not.Null);
        Assert.That(surface.ConsumeValue, Is.EqualTo(1ul));

        Frame(device, presenter, surface);
        Assert.That(surface.LatchedCopy, Is.SameAs(copy), "the copy texture is made once, not per frame");

        Produce(gd, surface, 2);
        Frame(device, presenter, surface);
        Assert.That(surface.ConsumeValue, Is.EqualTo(2ul));
    }

    // What a panel does: the producer exports, the consumer latches through its own import of the same memory.
    [Test]
    public void AnImport_CopiesTheFrame_AndReleasesItToTheProducer()
    {
        var device = CreateSharingDevice();
        var gd = (GraphicsDevice)device;
        var prms = new PresentationParameters(PresenterType.RenderTarget, 4, 4, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, prms, "shared_surface_import_latch");
        using var surface = SharedSurface.CreateExportable(device, 4, 4, SurfaceFormat.B8G8R8A8.UNorm);
        using var import = SharedSurface.Import(device, surface.Descriptor);
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.Presenter = presenter;

        Produce(gd, surface, 1);
        Frame(device, presenter, import);

        Assert.That(import.LatchedCopy, Is.Not.Null);
        Assert.That(surface.ConsumeValue, Is.EqualTo(1ul), "the producer's own handle sees the import's release");
    }

    // Two surfaces: the producer writes one while the panel copies the other. The panel copies the newest frame and gives
    // back both - the older one too, which nobody will read, or the producer could never write it again.
    [Test]
    public void OfTwoSurfaces_TheNewestFrameIsCopied_AndBothAreGivenBack()
    {
        var device = CreateSharingDevice();
        var gd = (GraphicsDevice)device;
        var prms = new PresentationParameters(PresenterType.RenderTarget, 4, 4, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, prms, "shared_surface_two_buffers");
        using var surface = SharedSurface.CreateExportable(device, 4, 4, SurfaceFormat.B8G8R8A8.UNorm, bufferCount: 2);
        using var import = SharedSurface.Import(device, surface.Descriptor);
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.Presenter = presenter;

        Assert.That(import.BufferCount, Is.EqualTo(2), "the import takes both surfaces");

        Produce(gd, surface.SurfaceAt(0), 1);
        Produce(gd, surface.SurfaceAt(1), 2);
        Frame(device, presenter, import);

        Assert.Multiple(() =>
        {
            Assert.That(import.LatchedCopy, Is.Not.Null);
            Assert.That(surface.SurfaceAt(1).ConsumeValue, Is.EqualTo(2ul), "the newest frame is the one copied");
            Assert.That(surface.SurfaceAt(0).ConsumeValue, Is.EqualTo(1ul), "the older frame is given back unread");
            Assert.That(import.LatchNewFrame(), Is.Zero, "and nothing newer is left");
        });

        Produce(gd, surface.SurfaceAt(0), 3);
        Frame(device, presenter, import);
        Assert.That(surface.SurfaceAt(0).ConsumeValue, Is.EqualTo(3ul), "the next frame, written over the older one, comes through");
    }

    // A panel on the producer's own logical device: the surface is taken as it is, with no handles - also on a device
    // that exports none - and the frame comes through and goes back as through an import.
    [Test]
    public void ASurfaceOfTheSameDevice_IsTakenAsItIs_CopiedAndGivenBack()
    {
        var device = GpuFixture.CreateRenderDevice();
        var gd = (GraphicsDevice)device;
        var prms = new PresentationParameters(PresenterType.RenderTarget, 4, 4, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, prms, "shared_surface_in_device");
        using var surface = SharedSurface.CreateInDevice(device, 4, 4, SurfaceFormat.B8G8R8A8.UNorm, bufferCount: 2);
        using var taken = SharedSurface.Import(device, surface.Descriptor);
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.Presenter = presenter;

        Assert.Multiple(() =>
        {
            Assert.That(surface.Descriptor.MemoryHandle, Is.EqualTo(IntPtr.Zero), "no handle is made");
            Assert.That(taken.BufferCount, Is.EqualTo(2));
            Assert.That(taken.SurfaceAt(1).ProduceSemaphore, Is.SameAs(surface.SurfaceAt(1).ProduceSemaphore));
        });

        Produce(gd, surface.SurfaceAt(0), 1);
        Produce(gd, surface.SurfaceAt(1), 2);
        Frame(device, presenter, taken);

        Assert.Multiple(() =>
        {
            Assert.That(taken.LatchedCopy, Is.Not.Null);
            Assert.That(surface.SurfaceAt(1).ConsumeValue, Is.EqualTo(2ul), "the newest frame is the one copied");
            Assert.That(surface.SurfaceAt(0).ConsumeValue, Is.EqualTo(1ul), "the older frame is given back unread");
        });
    }

    // The producer retires a surface while the panel still holds what it took: what it took stays alive until it lets go.
    [Test]
    public void ASurfaceOfTheSameDevice_DisposedByItsProducer_LastsUntilTheTakerLetsGo()
    {
        var device = GpuFixture.CreateRenderDevice();
        var gd = (GraphicsDevice)device;
        var surface = SharedSurface.CreateInDevice(device, 4, 4, SurfaceFormat.B8G8R8A8.UNorm);
        var taken = SharedSurface.Import(device, surface.Descriptor);
        Produce(gd, surface, 1);

        surface.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(surface.IsDisposed, Is.True);
            Assert.That(taken.ProduceValue, Is.EqualTo(1ul), "the timeline is still there to read");
            Assert.Throws<NotSupportedException>(() => SharedSurface.Import(device, surface.Descriptor),
                "a disposed surface is not taken again");
        });

        taken.Dispose();
        device.DeviceWaitIdle();
    }

    private static IGraphicsDevice CreateSharingDevice()
    {
        var device = GpuFixture.CreateRenderDevice();
        Assume.That(SharedSurface.CanExport(device) && SharedSurface.CanImport(device), Is.True,
            $"{device.Adapter.AdapterProperties.DeviceName} cannot share timeline semaphores");
        return device;
    }

    private static void Frame(IGraphicsDevice device, GraphicsPresenter presenter, SharedSurface surface)
    {
        Assert.That(device.BeginDraw(beforeRenderPass: _ => surface.LatchCopy(device)), Is.True);
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();
    }

    private static void Produce(GraphicsDevice device, SharedSurface surface, ulong frame)
    {
        var result = device.LogicalDevice.SignalSemaphore(new SemaphoreSignalInfo
        {
            Semaphore = surface.ProduceSemaphore,
            Value = frame
        });
        Assert.That(result, Is.EqualTo(Result.Success));
    }
}
