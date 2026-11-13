extern alias engine;
using System;
using System.IO;
using Adamantium.Core;
using Adamantium.Core.DependencyInjection;
using Adamantium.ECS;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using NUnit.Framework;
using engine::Adamantium.Engine.Screenshots;
using engine::Adamantium.Engine.Templates;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>An entity drawn alone to a picture by a rendering service of the universe it is taken in: all of it in view,
/// nothing but it over a transparent ground, the service asleep once its queue is empty.</summary>
[TestFixture]
public class ScreenshotRenderingServiceTests
{
    private const uint Size = 128;
    private const int MostFrames = 20;

    private static readonly string Figure =
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Models", "RiggedFigure", "RiggedFigure.gltf");

    private Universe _universe;
    private IScreenshotRenderingService _screenshots;

    [SetUp]
    public void MakeUniverse()
    {
        var container = new AdamantiumDependencyContainer();
        var registry = (IContainerRegistry)container;
        var host = new NoSurfaceHost();
        registry.RegisterInstance<IOutputFactory>(host);
        registry.RegisterInstance<IWindowingPlatform>(host);
        _universe = new Universe(UniverseMode.Slave, false, container, new SharedDeviceService());
        _universe.InitializeUniverse();
        _screenshots = ScreenshotRenderingService.Create(_universe.EntityWorld, Size);
        RunFrame();
    }

    [TearDown]
    public void ShutDown()
    {
        _universe.ShutDown();
        GpuFixture.ReleaseRenderDevices();
    }

    [Test]
    public void AModel_FillsThePicture_OverATransparentGround()
    {
        var picture = Take(LoadFigure());

        Assert.That(picture, Is.Not.Null);
        var drawn = OpaqueBounds(picture, "screenshot-figure.png");
        Assert.Multiple(() =>
        {
            Assert.That(Math.Max(drawn.Right - drawn.Left, drawn.Bottom - drawn.Top), Is.GreaterThan(Size / 2),
                "how large the model is drawn");
            Assert.That(drawn.Left, Is.GreaterThan(0), "a gap to the left");
            Assert.That(drawn.Top, Is.GreaterThan(0), "a gap above");
            Assert.That(drawn.Right, Is.LessThan(Size - 1), "a gap to the right");
            Assert.That(drawn.Bottom, Is.LessThan(Size - 1), "a gap below");
        });
    }

    [Test]
    public void TheSameModelTwice_GivesTheSamePicture_AndTheServiceSleepsAfter()
    {
        var first = OpaqueBounds(Take(LoadFigure()), null);
        var second = OpaqueBounds(Take(LoadFigure()), null);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(_screenshots.IsActive, Is.False, "asleep with nothing in its queue");
        });
    }

    [Test]
    public void TheEntityTaken_StaysOutOfTheWorld_AndOneWithNoBounds_GivesNoPicture()
    {
        var figure = LoadFigure();

        var pictured = Take(figure);
        var nothing = Take(new Entity(null, "Nothing"));

        Assert.Multiple(() =>
        {
            Assert.That(pictured, Is.Not.Null);
            Assert.That(nothing, Is.Null);
            Assert.That(_universe.EntityWorld.MainScene.RootEntities, Does.Not.Contain(figure));
        });
    }

    [Test]
    public void AScreenshotCancelledBeforeItIsTaken_IsLeftOut_AndTheNextIsTaken()
    {
        using var cancel = new System.Threading.CancellationTokenSource();
        var cancelled = _screenshots.Enqueue(LoadFigure(), cancel.Token);
        cancel.Cancel();

        var next = Take(LoadFigure());

        Assert.Multiple(() =>
        {
            Assert.That(cancelled.IsCanceled, Is.True);
            Assert.That(next, Is.Not.Null);
            Assert.That(_screenshots.IsActive, Is.False);
        });
    }

    private Entity LoadFigure()
    {
        var options = new AssetLoadOptions { AllowDuplication = true, IgnoreRootDirectory = true };
        var scene = _universe.Assets.Load<SceneData>(Figure, options);
        return _universe.EntityWorld
            .CreateEntityFromTemplate(new EntityImportTemplate(scene, _universe.Assets, Vector3<double>.Zero))
            .GetAwaiter().GetResult();
    }

    private Image Take(Entity entity)
    {
        var picture = _screenshots.Enqueue(entity);
        for (var frame = 0; frame < MostFrames && !picture.IsCompleted; frame++)
        {
            RunFrame();
        }

        Assert.That(picture.IsCompleted, Is.True, $"no picture in {MostFrames} frames");
        return picture.GetAwaiter().GetResult();
    }

    private void RunFrame()
    {
        _universe.RunOnce(default(AppTime));
        _universe.Submit();
    }

    private static (uint Left, uint Top, uint Right, uint Bottom) OpaqueBounds(Image picture, string savedAs)
    {
        using var stream = new MemoryStream();
        picture.Save(stream, ImageFileType.Png);
        if (savedAs != null)
        {
            File.WriteAllBytes(Path.Combine(TestContext.CurrentContext.WorkDirectory, savedAs), stream.ToArray());
        }

        stream.Position = 0;
        var bitmap = BitmapLoader.Load(stream);
        Assert.That((bitmap.Width, bitmap.Height), Is.EqualTo((Size, Size)), "the picture's size");
        var pixels = bitmap.GetRawPixels(0);
        var stride = (uint)(pixels.Length / (bitmap.Width * bitmap.Height));
        Assert.That(stride, Is.EqualTo(4), "bytes a pixel");
        Assert.That(pixels[3], Is.EqualTo(0), "the corner is transparent");

        uint left = uint.MaxValue, top = uint.MaxValue, right = 0, bottom = 0;
        for (uint y = 0; y < bitmap.Height; y++)
        {
            for (uint x = 0; x < bitmap.Width; x++)
            {
                if (pixels[(y * bitmap.Width + x) * stride + 3] < 128)
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
