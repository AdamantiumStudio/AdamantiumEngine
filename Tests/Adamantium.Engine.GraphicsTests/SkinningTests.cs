using System;
using System.IO;
using System.Runtime.CompilerServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using NUnit.Framework;
using Buffer = Adamantium.Graphics.Buffer;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>The skinning the effects share, on the GPU: a palette uploaded for the frame and read through a pointer, as
/// long as the skin has joints.</summary>
[TestFixture]
public class SkinningTests
{
    private const int Joints = 256;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    // A skin of more joints than a uniform buffer held once: the joint far along the palette carries its vertex, and a
    // vertex naming one past the palette stays where it is.
    [Test]
    public void AJointFarAlongALongPalette_CarriesItsVertex_AndOnePastThePalette_LeavesIt()
    {
        var palette = new Matrix4x4F[Joints];
        Array.Fill(palette, Matrix4x4F.Identity);
        palette[200] = Matrix4x4F.Translation(7, 0, 0);

        var bent = Bend(palette, [new Vector4F(200, 0, 0, 0), new Vector4F(Joints + 44, 0, 0, 0)]);

        Assert.Multiple(() =>
        {
            Assert.That(bent[0], Is.EqualTo(new Vector4F(8, 2, 3, 1)), "carried by joint 200");
            Assert.That(bent[1], Is.EqualTo(new Vector4F(1, 2, 3, 1)), "past the palette");
        });
    }

    private static Vector4F[] Bend(Matrix4x4F[] palette, Vector4F[] indices)
    {
        var device = GpuFixture.CreateRenderDevice();
        var gd = (GraphicsDevice)device;
        using var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "SkinningProbe.fx"), device);
        var pass = effect.Techniques[0].Passes[0];

        using var indicesBuffer = Storage(gd, indices);
        using var output = Storage(gd, new Vector4F[indices.Length]);
        effect.Parameters["JointCount"].SetValue((uint)palette.Length);
        effect.Parameters["IndicesAddress"].SetValue(indicesBuffer.GetDeviceAddress());
        effect.Parameters["OutputAddress"].SetValue(output.GetDeviceAddress());
        effect.Parameters["Point"].SetValue(new Vector3F(1, 2, 3));

        var prms = new PresentationParameters(PresenterType.RenderTarget, 16, 16, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, prms, "skinning_probe");
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.MSAALevel = presenter.MSAALevel;
        device.Presenter = presenter;

        Assert.That(device.BeginDraw(beforeRenderPass: _ =>
        {
            effect.Parameters["PaletteAddress"].SetValue(device.UploadFrameData<Matrix4x4F>(palette));
            pass.Apply();
            gd.Dispatch((uint)indices.Length);
            gd.BufferBarrier(output,
                PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
        }), Is.True);
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        var bent = new Vector4F[indices.Length];
        output.GetData(bent);
        return bent;
    }

    private static Buffer Storage<T>(GraphicsDevice device, T[] data) where T : unmanaged
    {
        var buffer = Buffer.New(device, (ulong)(data.Length * Unsafe.SizeOf<T>()),
            BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress,
            MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
        buffer.SetData(data);
        return buffer;
    }
}
