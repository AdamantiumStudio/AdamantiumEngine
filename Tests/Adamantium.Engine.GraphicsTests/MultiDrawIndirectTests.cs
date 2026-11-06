using System;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>
/// Many draws from one indirect command and no vertex buffer, each naming its own first instance: the left half of the
/// target drawn by the draw at instance 0, the right half by the one at instance 1, from an offset into the buffer.
/// </summary>
[TestFixture]
public class MultiDrawIndirectTests
{
    private const int Size = 64;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    // Each draw's six vertices make a quad over half the target, chosen by its first instance, which also picks the color.
    private const string Shader = """
        struct VSOut { float4 Position : SV_Position; nointerpolation uint Which : TEXCOORD0; };

        [shader("vertex")]
        VSOut VS(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID, uint firstInstance : SV_StartInstanceLocation)
        {
            uint which = max(instanceId, firstInstance);
            float2 corners[6] = { float2(0, 0), float2(1, 0), float2(0, 1), float2(0, 1), float2(1, 0), float2(1, 1) };
            float2 c = corners[vertexId % 6u];
            float x = (c.x + float(which)) * 0.5;   // which 0: [0, 0.5], which 1: [0.5, 1]
            VSOut o;
            o.Position = float4(x * 2.0 - 1.0, c.y * 2.0 - 1.0, 0.0, 1.0);
            o.Which = which;
            return o;
        }

        [shader("fragment")]
        float4 PS(VSOut i) : SV_Target { return i.Which == 0u ? float4(1, 0, 0, 1) : float4(0, 1, 0, 1); }

        technique T { pass { VertexShader = VS; PixelShader = PS; } }
        """;

    [Test]
    public void ManyDrawsFromOneIndirectCommand_EachDrawsWithItsOwnFirstInstance()
    {
        var device = GpuFixture.CreateRenderDevice();
        var gd = (GraphicsDevice)device;
        Assume.That(device.MainDevice.IsMultiDrawIndirectEnabled, "the device has no multiDrawIndirect");
        Assert.That(device.MainDevice.MaxDrawIndirectCount, Is.GreaterThan(1u));

        var compiled = Adamantium.EffectsCompiler.EffectCompiler.Compile(Shader, "MultiDrawIndirect.fx");
        Assert.That(compiled.HasErrors, Is.False, string.Join("\n", compiled.Logger.Messages));
        using var effect = new Effect(device, compiled.EffectData);

        // A command to skip first, so the offset is exercised: vertexCount, instanceCount, firstVertex, firstInstance.
        uint[] commands = [0, 0, 0, 0, 6, 1, 0, 0, 6, 1, 0, 1];
        using var indirect = Adamantium.Graphics.Buffer.New(gd, (ulong)(commands.Length * sizeof(uint)),
            BufferUsageFlags.IndirectBuffer, MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
        indirect.SetData(commands);

        var parameters = new PresentationParameters(PresenterType.RenderTarget, Size, Size, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, parameters, "multi_draw_indirect");
        var target = (Texture)presenter.RenderTarget.ResolveTexture;
        device.ClearColor = Colors.Black;
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.MSAALevel = presenter.MSAALevel;
        device.Presenter = presenter;
        device.CullMode = CullModeFlagBits.None;
        gd.ColorBlendEnabled = false;

        Assert.That(device.BeginDraw(), Is.True);
        device.SetViewports(new Viewport { Width = Size, Height = Size, MinDepth = 0, MaxDepth = 1 });
        device.SetScissors(new Rect2D { Extent = new Extent2D { Width = Size, Height = Size } });
        device.VertexType = null;
        device.PrimitiveTopology = PrimitiveTopology.TriangleList;
        effect.Techniques[0].Passes[0].Apply();
        gd.DrawIndirect(indirect, 16, 2);
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        // BGRA: red is byte 2, green byte 1.
        using var image = target.ToImage();
        var left = new byte[4];
        var right = new byte[4];
        Marshal.Copy(image.DataPointer + (Size / 2 * Size + Size / 4) * 4, left, 0, 4);
        Marshal.Copy(image.DataPointer + (Size / 2 * Size + Size * 3 / 4) * 4, right, 0, 4);
        Assert.Multiple(() =>
        {
            Assert.That(left[2], Is.GreaterThan(200), "the draw at instance 0 paints the left half red");
            Assert.That(right[1], Is.GreaterThan(200), "the draw at instance 1 paints the right half green");
        });
    }

    [Test]
    public void CommandsPastTheBuffer_OrOffTheirBoundary_AreRefused()
    {
        var device = GpuFixture.CreateRenderDevice();
        var gd = (GraphicsDevice)device;
        var compiled = Adamantium.EffectsCompiler.EffectCompiler.Compile(Shader, "MultiDrawIndirect.fx");
        using var effect = new Effect(device, compiled.EffectData);
        using var indirect = Adamantium.Graphics.Buffer.New(gd, 32, BufferUsageFlags.IndirectBuffer,
            MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
        effect.Techniques[0].Passes[0].Apply();

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => gd.DrawIndirect(indirect, 2, 1), "an offset off a 4-byte boundary");
            Assert.Throws<ArgumentException>(() => gd.DrawIndirect(indirect, 32, 1), "a command past the buffer's end");
            if (device.MainDevice.IsMultiDrawIndirectEnabled)
            {
                Assert.Throws<ArgumentException>(() => gd.DrawIndirect(indirect, 0, 2, 8), "a stride no command fits in");
                Assert.Throws<ArgumentException>(() => gd.DrawIndirect(indirect, 16, 2), "the second command past the end");
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => gd.DrawIndirect(indirect, 0, 2), "two draws at once");
            }
        });
    }
}
