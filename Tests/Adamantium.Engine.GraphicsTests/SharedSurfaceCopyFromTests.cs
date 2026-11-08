using System;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>
/// A producer whose frames are textures of another API on the same adapter - a web page drawn by Chromium into a D3D11
/// texture - has them copied into a surface of the consumer's device: what the device takes, and what is refused before
/// the handle is touched. The copy itself is checked where such a texture is made, by the WebView's tests.
/// </summary>
[TestFixture]
public class SharedSurfaceCopyFromTests
{
    private const uint Size = 4;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void TheAdapter_OnWindows_IsNamedByItsLuid()
    {
        Assume.That(OperatingSystem.IsWindows(), "a LUID names an adapter on Windows");
        var device = GpuFixture.CreateRenderDevice();

        Assert.That(device.Adapter.Luid, Is.Not.Null.And.Not.EqualTo(0L));
    }

    [Test]
    public void AD3D11Texture_InTheSurfacesFormat_IsTaken_WhereTheDeviceImportsWin32Memory()
    {
        var device = GpuFixture.CreateRenderDevice();
        var imports = OperatingSystem.IsWindows()
                      && device.MainDevice.IsExtensionEnabled(Constants.VK_KHR_EXTERNAL_MEMORY_WIN32_EXTENSION_NAME);

        Assert.That(SharedSurface.CanCopyFrom(device, SharedHandleType.D3D11Texture, SurfaceFormat.B8G8R8A8.UNorm),
            Is.EqualTo(imports));
    }

    [Test]
    public void AnOpaqueHandle_IsNotATextureToCopyFrom()
    {
        var device = GpuFixture.CreateRenderDevice();

        Assert.Multiple(() =>
        {
            Assert.That(SharedSurface.CanCopyFrom(device, SharedHandleType.OpaqueWin32, SurfaceFormat.B8G8R8A8.UNorm), Is.False);
            Assert.That(SharedSurface.CanCopyFrom(device, SharedHandleType.OpaqueFd, SurfaceFormat.B8G8R8A8.UNorm), Is.False);
        });
    }

    [Test]
    public void RegionsOutsideTheSurface_CopyNothing_SayNothingWasWritten_AndLeaveTheHandleAlone()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var surface = SharedSurface.CreateInDevice(device, Size, Size, SurfaceFormat.B8G8R8A8.UNorm);

        var wrote = surface.CopyFrom(device, IntPtr.Zero, SharedHandleType.D3D11Texture, Size, Size,
            [new Rectangle<int>((int)Size, 0, 2, 2), new Rectangle<int>(0, 0, 0, 3)], 1);

        Assert.Multiple(() =>
        {
            Assert.That(wrote, Is.False);
            Assert.That(surface.ProduceValue, Is.EqualTo(0ul));
            Assert.That(surface.WriteRegions(device, [new TextureRegionUpdate(0, 0, 0, 0, 1, 1, new byte[4])], 1),
                Is.True, "frame 1 is still the next to write");
        });
    }

    [Test]
    public void AFrame_NotPastTheLastOneWritten_IsRefused_BeforeTheHandleIsTouched()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var surface = SharedSurface.CreateInDevice(device, Size, Size, SurfaceFormat.B8G8R8A8.UNorm);

        Assert.Throws<ArgumentOutOfRangeException>(() => surface.CopyFrom(device, IntPtr.Zero,
            SharedHandleType.D3D11Texture, Size, Size, [new Rectangle<int>(0, 0, 2, 2)], 0));
    }

    [Test]
    public void TheConsumersSide_IsNotCopiedInto()
    {
        var device = GpuFixture.CreateRenderDevice();
        using var producer = SharedSurface.CreateInDevice(device, Size, Size, SurfaceFormat.B8G8R8A8.UNorm);
        using var consumer = SharedSurface.Import(device, producer.Descriptor);

        Assert.Throws<InvalidOperationException>(() => consumer.CopyFrom(device, IntPtr.Zero,
            SharedHandleType.D3D11Texture, Size, Size, [new Rectangle<int>(0, 0, 2, 2)], 1));
    }
}
