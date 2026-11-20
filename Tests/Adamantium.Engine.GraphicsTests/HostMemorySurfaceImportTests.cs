using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>Frames a producer copies into memory of the process, taken by a consumer on another device: imported as a
/// shared surface, the newest frame lands in it as each one comes - from the slots imported as buffers, or through a
/// staging buffer where the device takes no memory of the process.</summary>
[TestFixture]
public class HostMemorySurfaceImportTests
{
    private const uint Size = 8;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void AnImportedRing_ShowsEachNewFrame_AsItComes()
    {
        var device = GpuFixture.CreateRenderDevice();
        Assume.That(HostMemorySurfaceProducer.CanProduce(device), "the device copies no frames into memory of the process");
        using var presenter = CreatePresenter(device);
        var consumer = new RecordingSurfaceConsumer();
        using var producer = new HostMemorySurfaceProducer(consumer, SurfaceFormat.B8G8R8A8.UNorm);
        device.ClearColor = Colors.Red;
        Produce(device, presenter, producer);
        Produce(device, presenter, producer);
        Assert.That(consumer.Bound, Has.Count.EqualTo(1), "the ring is handed over with its first frame");

        var viewer = GpuFixture.CreateRenderDevice();
        using var viewerPresenter = CreatePresenter(viewer);
        using var taken = SharedSurface.Import(viewer, consumer.Bound[0]);
        Assert.That(taken.LatchedCopy, Is.Null, "nothing before the first latch");
        View(viewer, viewerPresenter, taken);
        var first = PixelAt(taken.LatchedCopy, 3, 5);

        device.ClearColor = Colors.Lime;
        Produce(device, presenter, producer);
        View(viewer, viewerPresenter, taken);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "the first frame, red");
            Assert.That(PixelAt(taken.LatchedCopy, 3, 5), Is.EqualTo(new byte[] { 0, 255, 0, 255 }), "the next, green");
        });
    }

    [Test]
    public void ThroughAStagingBuffer_TheFramesLandTheSame_AndTheSlotsAreGivenBack()
    {
        var device = GpuFixture.CreateRenderDevice();
        Assume.That(HostMemorySurfaceProducer.CanProduce(device), "the device copies no frames into memory of the process");
        using var presenter = CreatePresenter(device);
        var consumer = new RecordingSurfaceConsumer();
        using var producer = new HostMemorySurfaceProducer(consumer, SurfaceFormat.B8G8R8A8.UNorm);
        device.ClearColor = Colors.Red;
        Produce(device, presenter, producer);
        Produce(device, presenter, producer);

        var viewer = GpuFixture.CreateRenderDevice();
        using var target = SharedSurface.CreateInDevice(viewer, Size, Size, SurfaceFormat.B8G8R8A8.UNorm);
        using var import = new HostFrameImport(viewer, consumer.Bound[0], importSlots: false);
        var firstFrame = import.LatchInto(viewer, target);
        viewer.DeviceWaitIdle();
        var first = PixelAt(target, 3, 5);

        device.ClearColor = Colors.Lime;
        ulong[] latched = new ulong[4];
        for (var frame = 0; frame < latched.Length; frame++)
        {
            Produce(device, presenter, producer);
            latched[frame] = import.LatchInto(viewer, target);
            viewer.DeviceWaitIdle();
        }

        Assert.Multiple(() =>
        {
            Assert.That(import.ImportsSlots, Is.False);
            Assert.That(firstFrame, Is.EqualTo(2ul), "the newer of the two frames made");
            Assert.That(first, Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "the first frame, red");
            Assert.That(PixelAt(target, 3, 5), Is.EqualTo(new byte[] { 0, 255, 0, 255 }), "the newest, green");
            Assert.That(latched, Is.EqualTo(new ulong[] { 3, 4, 5, 6 }), "more frames than slots: they were given back");
        });
    }

    private static GraphicsPresenter CreatePresenter(IGraphicsDevice device)
    {
        var parameters = new PresentationParameters(PresenterType.RenderTarget, Size, Size, IntPtr.Zero);
        return GraphicsPresenter.Create(device, parameters, "host_memory_import");
    }

    private static void Produce(IGraphicsDevice device, GraphicsPresenter presenter, HostMemorySurfaceProducer producer)
    {
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.Presenter = presenter;
        Assert.That(device.BeginDraw(), Is.True);
        device.EndDraw();
        producer.Produce(device, ((RenderTargetGraphicsPresenter)presenter).ResolveTexture, Size, Size, inDevice: false);
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        producer.NoteSubmitted();
        device.DeviceWaitIdle();
        producer.NoteSubmitted();
    }

    private static void View(IGraphicsDevice device, GraphicsPresenter presenter, SharedSurface surface)
    {
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.Presenter = presenter;
        Assert.That(device.BeginDraw(beforeRenderPass: _ => surface.LatchCopy(device)), Is.True);
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();
    }

    private static byte[] PixelAt(ITexture texture, int x, int y)
    {
        using var image = texture.ToImage(0, 0);
        var pixel = new byte[4];
        Marshal.Copy(image.DataPointer + (int)((y * Size + x) * 4), pixel, 0, 4);
        return pixel;
    }
}
