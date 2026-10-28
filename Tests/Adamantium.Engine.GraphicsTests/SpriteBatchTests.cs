using System;
using System.IO;
using System.Runtime.InteropServices;
using Adamantium.FX;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using NUnit.Framework;
using Image = Adamantium.Imaging.Image;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>A sprite batch draws sprites of several textures, and of none, in one batch, each in its own texture; sorted
/// by depth, the back first or the front first, and kept in front by the depth test; past its capacity and beside
/// another batch of the frame, every sprite as it was given; a texture disposed before the batch ends drawn as the
/// stand-in, the device alive.</summary>
[TestFixture]
public class SpriteBatchTests
{
    private const int Size = 64;

    private static readonly Color Red = new(255, 0, 0);
    private static readonly Color Green = new(0, 255, 0);
    private static readonly Color Blue = new(0, 0, 255);
    private static readonly Color Black = new(0, 0, 0);

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void SpritesOfTwoTexturesAndOfNone_AreEachDrawnInTheirOwn()
    {
        var pixels = Render((batch, red, blue, _) =>
        {
            batch.Begin(new Vector2<double>(Size, Size));
            batch.Draw(red, new RectangleF(0, 0, Size / 2, Size / 2), Colors.White);
            batch.Draw(blue, new RectangleF(Size / 2, 0, Size / 2, Size / 2), Colors.White);
            batch.Draw(new RectangleF(0, Size / 2, Size, Size / 2), Green, 0, Vector2F.Zero);
            batch.End();
        });

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(pixels, 16, 16), Is.EqualTo(Red), "the first texture");
            Assert.That(Pixel(pixels, 48, 16), Is.EqualTo(Blue), "the second texture");
            Assert.That(Pixel(pixels, 32, 48), Is.EqualTo(Green), "no texture");
        });
    }

    // Drawn last, a sprite covers the one under it: back to front leaves the front one, front to back the back one.
    [TestCase(SpriteSortMode.BackToFront, 0, 255)]
    [TestCase(SpriteSortMode.FrontToBack, 255, 0)]
    [TestCase(SpriteSortMode.NoSort, 255, 0)]
    public void SpritesSortedByDepth_AreDrawnInTheirOrder(SpriteSortMode mode, int red, int green)
    {
        var pixels = Render((batch, _, _, _) =>
        {
            batch.Begin(new Vector2<double>(Size, Size), mode);
            batch.Draw(new RectangleF(0, 0, Size, Size), Green, 0, Vector2F.Zero, layerDepth: 0);
            batch.Draw(new RectangleF(0, 0, Size, Size), Red, 0, Vector2F.Zero, layerDepth: 1);
            batch.End();
        });

        Assert.That(Pixel(pixels, 32, 32), Is.EqualTo(new Color(red, green, 0)));
    }

    [Test]
    public void TheNearestSprite_StaysInFront_UnderTheDepthTest()
    {
        var pixels = Render((batch, _, _, _) =>
        {
            batch.Begin(new Vector2<double>(Size, Size));
            batch.Draw(new RectangleF(0, 0, Size, Size), Green, 0, Vector2F.Zero, layerDepth: 0);
            batch.Draw(new RectangleF(0, 0, Size, Size), Red, 0, Vector2F.Zero, layerDepth: 1);
            batch.End();
        }, depthTest: true);

        Assert.That(Pixel(pixels, 32, 32), Is.EqualTo(Green));
    }

    [Test]
    public void SpritesPastTheCapacity_AreAllDrawnAsGiven()
    {
        var pixels = Render((batch, _, _, _) =>
        {
            batch.Begin(new Vector2<double>(Size, Size));
            batch.Draw(new RectangleF(0, 0, Size / 2, Size), Red, 0, Vector2F.Zero);
            for (var i = 0; i < 1023; i++)
            {
                batch.Draw(new RectangleF(0, 0, 0, 0), Blue, 0, Vector2F.Zero);
            }

            batch.Draw(new RectangleF(Size / 2, 0, Size / 2, Size), Green, 0, Vector2F.Zero);
            batch.End();
        }, capacity: 1024);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(pixels, 16, 32), Is.EqualTo(Red), "the first sprite");
            Assert.That(Pixel(pixels, 48, 32), Is.EqualTo(Green), "the one past the capacity");
        });
    }

    [Test]
    public void TwoBatchesOfAFrame_EachKeepTheirSprites()
    {
        var pixels = Render((batch, _, _, _) =>
        {
            batch.Begin(new Vector2<double>(Size, Size));
            batch.Draw(new RectangleF(0, 0, Size / 2, Size), Red, 0, Vector2F.Zero);
            batch.End();
            batch.Begin(new Vector2<double>(Size, Size));
            batch.Draw(new RectangleF(Size / 2, 0, Size / 2, Size), Blue, 0, Vector2F.Zero);
            batch.End();
        });

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(pixels, 16, 32), Is.EqualTo(Red), "the first batch");
            Assert.That(Pixel(pixels, 48, 32), Is.EqualTo(Blue), "the second batch");
        });
    }

    // The stand-in is red in a debug build and clear in a release one, so the frame after it tells: drawn, the device
    // alive, and the validation layer, where it runs, silent.
    [Test]
    public void ATextureDisposedBeforeTheBatchEnds_IsDrawnAsTheStandIn_TheDeviceAlive()
    {
        var before = VulkanInstance.RecentValidationMessages;
        var pixels = Render((batch, red, _, _) =>
        {
            batch.Begin(new Vector2<double>(Size, Size));
            batch.Draw(red, new RectangleF(0, 0, Size, Size), Colors.White);
            red.Dispose();
            batch.End();
        }, next: (batch, _, blue, _) =>
        {
            batch.Begin(new Vector2<double>(Size, Size));
            batch.Draw(blue, new RectangleF(0, 0, Size, Size), Colors.White);
            batch.End();
        });

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(pixels, 32, 32), Is.EqualTo(Blue), "the next frame");
            Assert.That(VulkanInstance.RecentValidationMessages, Is.EqualTo(before));
        });
    }

    [Test]
    public void ACustomEffect_DrawsAsTheSpriteEffectDoes()
    {
        var pixels = Render((batch, red, blue, device) =>
        {
            using var effect = new SpriteEffect(device);
            effect.CurrentTechnique = effect.Techniques[0];
            batch.Begin(new Vector2<double>(Size, Size), SpriteSortMode.NoSort, effect);
            batch.Draw(red, new RectangleF(0, 0, Size / 2, Size), Colors.White);
            batch.Draw(blue, new RectangleF(Size / 2, 0, Size / 2, Size), Colors.White);
            batch.End();
        });

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(pixels, 16, 32), Is.EqualTo(Red));
            Assert.That(Pixel(pixels, 48, 32), Is.EqualTo(Blue));
        });
    }

    private static byte[] Render(Action<SpriteBatch, Texture, Texture, GraphicsDevice> draw, bool depthTest = false,
        uint capacity = 4096, Action<SpriteBatch, Texture, Texture, GraphicsDevice> next = null)
    {
        var device = GpuFixture.CreateRenderDevice();
        var gd = (GraphicsDevice)device;
        using var red = Solid(gd, Red);
        using var blue = Solid(gd, Blue);
        using var batch = new SpriteBatch(device, capacity);
        var parameters = new PresentationParameters(PresenterType.RenderTarget, Size, Size, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, parameters, "sprites");
        var target = (Texture)presenter.RenderTarget.ResolveTexture;
        device.ClearColor = Colors.Black;
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.MSAALevel = presenter.MSAALevel;
        device.Presenter = presenter;
        device.DepthTestEnabled = depthTest;
        gd.ColorBlendEnabled = false;

        foreach (var frame in next == null ? [draw] : new[] { draw, next })
        {
            Assert.That(device.BeginDraw(), Is.True);
            device.SetViewports(new Viewport { Width = Size, Height = Size, MinDepth = 0, MaxDepth = 1 });
            device.SetScissors(new Rect2D { Offset = new Offset2D(), Extent = new Extent2D { Width = Size, Height = Size } });
            frame(batch, red, blue, gd);
            device.EndDraw();
            device.Submit();
            presenter.Present();
            device.FrameEnded();
        }

        device.DeviceWaitIdle();

        using var image = target.ReadbackToImage();
        var bytes = new byte[image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private static Texture Solid(GraphicsDevice device, Color color)
    {
        using var image = Image.New2D(4, 4, Format.R8G8B8A8_UNORM);
        var texels = new byte[image.TotalSizeInBytes];
        for (var i = 0; i < texels.Length; i += 4)
        {
            (texels[i], texels[i + 1], texels[i + 2], texels[i + 3]) = (color.R, color.G, color.B, 255);
        }

        Marshal.Copy(texels, 0, image.DataPointer, texels.Length);
        var png = new MemoryStream();
        image.Save(png, ImageFileType.Png);
        png.Position = 0;
        return Texture.Load(device, png);
    }

    private static Color Pixel(byte[] bgra, int x, int y)
    {
        var at = (y * Size + x) * 4;
        return new Color(bgra[at + 2], bgra[at + 1], bgra[at]);
    }
}
