using System;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>An output's frames sent to a consumer through a ring of shared surfaces: a new ring is handed over only with
/// its first frame in it, frames go to the surface the consumer gave back, and a new size starts a new ring.</summary>
[TestFixture]
public class SharedSurfaceProducerTests
{
    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void TheFirstRing_IsHandedOver_OnlyOnceItHoldsAFrame()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var presenter = CreatePresenter(device, 8, 8);
        var consumer = new RecordingSurfaceConsumer();
        using var producer = new SharedSurfaceProducer(consumer, SurfaceFormat.B8G8R8A8.UNorm);

        Frame(device, presenter, producer, 8, 8);
        Assert.Multiple(() =>
        {
            Assert.That(consumer.Bound, Is.Empty, "the new ring is not handed over before its frame is done");
            Assert.That(consumer.FramesAsked, Is.EqualTo(1), "another frame is asked for, to hand it over");
            Assert.That(consumer.FramesSubmitted, Is.EqualTo(1), "its first frame is on its way");
        });

        Frame(device, presenter, producer, 8, 8);
        Assert.Multiple(() =>
        {
            Assert.That(consumer.Bound, Has.Count.EqualTo(1));
            Assert.That(consumer.Bound[0], Is.SameAs(producer.Surface.Descriptor));
            Assert.That(producer.Surface.SurfaceAt(1).ProduceValue, Is.EqualTo(2ul), "the next frame goes to the next surface");
        });
    }

    // Three surfaces: the producer writes only one the consumer has given back, the oldest.
    [Test]
    public void AFrame_GoesToTheSurfaceTheConsumerGaveBack_AndNoneWhileItHoldsThemAll()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var presenter = CreatePresenter(device, 8, 8);
        var consumer = new RecordingSurfaceConsumer();
        using var producer = new SharedSurfaceProducer(consumer, SurfaceFormat.B8G8R8A8.UNorm);
        Frame(device, presenter, producer, 8, 8);
        Frame(device, presenter, producer, 8, 8);
        Frame(device, presenter, producer, 8, 8);
        var ring = producer.Surface;

        Frame(device, presenter, producer, 8, 8);
        Assert.That(ring.SurfaceAt(0).ProduceValue, Is.EqualTo(1ul), "the consumer still holds the oldest: nothing written");

        GiveBack((GraphicsDevice)device, ring.SurfaceAt(0), 1);
        Frame(device, presenter, producer, 8, 8);
        Assert.That(ring.SurfaceAt(0).ProduceValue, Is.EqualTo(4ul), "given back, the oldest takes the next frame");
    }

    [Test]
    public void ANewSize_StartsANewRing_HandedOverWithItsFrame_AndLetsTheOldOneGo()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var small = CreatePresenter(device, 8, 8);
        using var large = CreatePresenter(device, 16, 16);
        var consumer = new RecordingSurfaceConsumer();
        var producer = new SharedSurfaceProducer(consumer, SurfaceFormat.B8G8R8A8.UNorm);
        Frame(device, small, producer, 8, 8);
        Frame(device, small, producer, 8, 8);
        var first = producer.Surface;

        Frame(device, large, producer, 16, 16);
        Assert.That(producer.Surface, Is.SameAs(first), "the old ring is kept until the new one holds a frame");

        Frame(device, large, producer, 16, 16);
        Assert.Multiple(() =>
        {
            Assert.That(producer.Surface.Width, Is.EqualTo(16u));
            Assert.That(consumer.Bound, Has.Count.EqualTo(2));
            Assert.That(consumer.Bound[1], Is.SameAs(producer.Surface.Descriptor));
        });

        var last = producer.Surface.Descriptor;
        producer.Release(retire: true);
        Assert.That(consumer.Unbound, Is.EqualTo((SharedSurfaceDescriptor[])[last]), "the consumer lets go of the ring");
        device.DeviceWaitIdle();
    }

    private static GraphicsPresenter CreatePresenter(IGraphicsDevice device, uint width, uint height)
    {
        var parameters = new PresentationParameters(PresenterType.RenderTarget, width, height, IntPtr.Zero);
        return GraphicsPresenter.Create(device, parameters, $"producer_{width}x{height}");
    }

    private static void Frame(IGraphicsDevice device, GraphicsPresenter presenter, SharedSurfaceProducer producer,
        uint width, uint height)
    {
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.Presenter = presenter;
        Assert.That(device.BeginDraw(), Is.True);
        device.EndDraw();
        producer.Produce(device, ((RenderTargetGraphicsPresenter)presenter).ResolveTexture, width, height, inDevice: true);
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        producer.NoteSubmitted();
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
}
