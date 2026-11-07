using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>
/// A producer whose frames are on the CPU - a web page drawn into memory - writes them into a surface of the consumer's
/// own device: the regions land in the surface, the frame on its produce timeline, and the consumer copies it as it
/// copies a frame drawn on the GPU.
/// </summary>
[TestFixture]
public class SharedSurfaceWriteTests
{
    private const uint Size = 4;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void AWrittenFrame_LandsInTheSurface_AndOnItsProduceTimeline()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var surface = SharedSurface.CreateInDevice(device, Size, Size, SurfaceFormat.B8G8R8A8.UNorm);

        surface.WriteRegions(device, [Whole(0, 0, 255)], 1);
        device.DeviceWaitIdle();

        Assert.Multiple(() =>
        {
            Assert.That(surface.ProduceValue, Is.EqualTo(1ul));
            Assert.That(PixelAt(surface, 2, 3), Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
        });
    }

    // A page sends the rectangles that changed: the next frame of the same surface writes those and leaves the rest.
    [Test]
    public void TheNextFrame_WritesItsRegions_AndLeavesTheRestAsItWas()
    {
        var device = (GraphicsDevice)GpuFixture.CreateRenderDevice();
        using var surface = SharedSurface.CreateInDevice(device, Size, Size, SurfaceFormat.B8G8R8A8.UNorm);
        surface.WriteRegions(device, [Whole(0, 0, 255)], 1);
        device.DeviceWaitIdle();
        GiveBack(device, surface, 1);

        surface.WriteRegions(device, [new TextureRegionUpdate(0, 0, 1, 1, 2, 2, Solid(2, 2, 0, 255, 0))], 2);
        device.DeviceWaitIdle();

        Assert.Multiple(() =>
        {
            Assert.That(surface.ProduceValue, Is.EqualTo(2ul));
            Assert.That(PixelAt(surface, 1, 1), Is.EqualTo(new byte[] { 0, 255, 0, 255 }), "in the region");
            Assert.That(PixelAt(surface, 2, 2), Is.EqualTo(new byte[] { 0, 255, 0, 255 }), "in the region");
            Assert.That(PixelAt(surface, 0, 0), Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "outside it, as before");
            Assert.That(PixelAt(surface, 3, 3), Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "outside it, as before");
        });
    }

    // What the panel does with it, frame after frame: copies the newest, gives the surfaces back, and the producer
    // writes the next frame into one given back.
    [Test]
    public void WrittenFrames_AreWhatTheConsumerCopies_FrameAfterFrame()
    {
        var device = GpuFixture.CreateRenderDevice();
        var prms = new PresentationParameters(PresenterType.RenderTarget, Size, Size, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, prms, "shared_surface_write");
        using var surface = SharedSurface.CreateInDevice(device, Size, Size, SurfaceFormat.B8G8R8A8.UNorm, bufferCount: 2);
        using var taken = SharedSurface.Import(device, surface.Descriptor);
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.Presenter = presenter;

        surface.SurfaceAt(0).WriteRegions(device, [Whole(255, 0, 0)], 1);
        device.DeviceWaitIdle();
        Frame(device, presenter, taken);
        var first = PixelAt(taken.LatchedCopy, 1, 2);

        Assert.That(surface.SurfaceAt(0).ConsumeValue, Is.EqualTo(1ul), "given back to the producer");
        surface.SurfaceAt(0).WriteRegions(device, [Whole(0, 255, 0)], 2);
        device.DeviceWaitIdle();
        Frame(device, presenter, taken);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(new byte[] { 255, 0, 0, 255 }), "the first frame");
            Assert.That(PixelAt(taken.LatchedCopy, 1, 2), Is.EqualTo(new byte[] { 0, 255, 0, 255 }), "the second");
            Assert.That(surface.SurfaceAt(0).ConsumeValue, Is.EqualTo(2ul));
        });
    }

    // Written again before the consumer gave it back, the surface could be overwritten while it is copied - and the
    // write would wait on the queue for a give-back that may never come.
    [Test]
    public void ASurfaceNotGivenBack_IsNotWrittenAgain()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var surface = SharedSurface.CreateInDevice(device, Size, Size, SurfaceFormat.B8G8R8A8.UNorm);
        surface.WriteRegions(device, [Whole(0, 0, 255)], 1);
        device.DeviceWaitIdle();

        Assert.Throws<InvalidOperationException>(() => surface.WriteRegions(device, [Whole(0, 255, 0)], 2));
    }

    [Test]
    public void AFrame_NotPastTheLastOneWritten_IsRefused()
    {
        var device = (GraphicsDevice)GpuFixture.CreateRenderDevice();
        using var surface = SharedSurface.CreateInDevice(device, Size, Size, SurfaceFormat.B8G8R8A8.UNorm);
        surface.WriteRegions(device, [Whole(0, 0, 255)], 3);
        device.DeviceWaitIdle();
        GiveBack(device, surface, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => surface.WriteRegions(device, [Whole(0, 0, 255)], 3));
    }

    // The consumer's side of the surface shares the producer's timelines; written through it, the producer's frames
    // would be numbered by another count.
    [Test]
    public void TheConsumersSide_IsNotWritten()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var surface = SharedSurface.CreateInDevice(device, Size, Size, SurfaceFormat.B8G8R8A8.UNorm);
        using var taken = SharedSurface.Import(device, surface.Descriptor);

        Assert.Throws<InvalidOperationException>(() => taken.WriteRegions(device, [Whole(0, 0, 255)], 1));
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

    private static void GiveBack(GraphicsDevice device, SharedSurface surface, ulong frame)
    {
        var result = device.LogicalDevice.SignalSemaphore(new SemaphoreSignalInfo
        {
            Semaphore = surface.ConsumeSemaphore,
            Value = frame
        });
        Assert.That(result, Is.EqualTo(Result.Success));
    }

    private static TextureRegionUpdate Whole(byte b, byte g, byte r) =>
        new(0, 0, 0, 0, Size, Size, Solid(Size, Size, b, g, r));

    private static byte[] Solid(uint width, uint height, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        return pixels;
    }

    private static byte[] PixelAt(ITexture texture, int x, int y)
    {
        using var image = texture.ToImage(0, 0);
        var pixel = new byte[4];
        Marshal.Copy(image.DataPointer + (int)((y * Size + x) * 4), pixel, 0, 4);
        return pixel;
    }
}
