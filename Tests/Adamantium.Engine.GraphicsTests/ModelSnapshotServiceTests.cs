extern alias engine;
using System;
using System.IO;
using Adamantium.Core.DependencyInjection;
using Adamantium.Imaging;
using Adamantium.Multiverse;
using NUnit.Framework;
using engine::Adamantium.Engine.Snapshots;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A model drawn alone to a picture: all of it in view, nothing but the model over a transparent ground.</summary>
[TestFixture]
public class ModelSnapshotServiceTests
{
    private const uint Size = 128;

    private static readonly string Figure =
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Models", "RiggedFigure", "RiggedFigure.gltf");

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void AModel_FillsThePicture_OverATransparentGround()
    {
        using var snapshots = CreateService();

        var png = snapshots.TakePng(Figure);

        Assert.That(png, Is.Not.Null);
        File.WriteAllBytes(Path.Combine(TestContext.CurrentContext.WorkDirectory, "snapshot-figure.png"), png);
        var picture = BitmapLoader.Load(new MemoryStream(png));
        var drawn = OpaqueBounds(picture);
        Assert.Multiple(() =>
        {
            Assert.That(picture.Width, Is.EqualTo(Size));
            Assert.That(picture.Height, Is.EqualTo(Size));
            Assert.That(Math.Max(drawn.Right - drawn.Left, drawn.Bottom - drawn.Top), Is.GreaterThan(Size / 2),
                "how large the model is drawn");
            Assert.That(drawn.Left, Is.GreaterThan(0), "a gap to the left");
            Assert.That(drawn.Top, Is.GreaterThan(0), "a gap above");
            Assert.That(drawn.Right, Is.LessThan(Size - 1), "a gap to the right");
            Assert.That(drawn.Bottom, Is.LessThan(Size - 1), "a gap below");
        });
    }

    // Nothing of a snapshot stays in the service's universe for the next: the same model gives the same picture again.
    [Test]
    public void TheSameModelTwice_GivesTheSamePicture()
    {
        using var snapshots = CreateService();

        var first = OpaqueBounds(BitmapLoader.Load(new MemoryStream(snapshots.TakePng(Figure))));
        var second = OpaqueBounds(BitmapLoader.Load(new MemoryStream(snapshots.TakePng(Figure))));

        Assert.That(second, Is.EqualTo(first));
    }

    private static ModelSnapshotService CreateService()
    {
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var host = new NoSurfaceHost();
        registry.RegisterInstance<IOutputFactory>(host);
        registry.RegisterInstance<IWindowingPlatform>(host);
        return new ModelSnapshotService(container, new SharedDeviceService(), Size);
    }

    private static (uint Left, uint Top, uint Right, uint Bottom) OpaqueBounds(IRawBitmap picture)
    {
        var pixels = picture.GetRawPixels(0);
        var stride = (uint)(pixels.Length / (picture.Width * picture.Height));
        Assert.That(stride, Is.EqualTo(4), "bytes a pixel");
        Assert.That(pixels[3], Is.EqualTo(0), "the corner is transparent");

        uint left = uint.MaxValue, top = uint.MaxValue, right = 0, bottom = 0;
        for (uint y = 0; y < picture.Height; y++)
        {
            for (uint x = 0; x < picture.Width; x++)
            {
                if (pixels[(y * picture.Width + x) * stride + 3] < 128)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        Assert.That(left, Is.Not.EqualTo(uint.MaxValue), "nothing was drawn");
        return (left, top, right, bottom);
    }
}
