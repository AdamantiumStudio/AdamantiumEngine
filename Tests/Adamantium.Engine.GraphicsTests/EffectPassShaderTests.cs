using System;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>The shaders of a pass are made once for its data on the main device: every effect of that data, on any render
/// device, holds the same, and the last effect to let go of them retires them.</summary>
[TestFixture]
public class EffectPassShaderTests
{
    private const string Source = """
        cbuffer Frame { float4 Tint; };
        float4 VS(float4 p : POSITION) : SV_Position { return p; }
        float4 PS(float4 p : SV_Position) : SV_Target { return Tint; }
        float4 Dim(float4 p : SV_Position) : SV_Target { return Tint * 0.5; }
        technique T { pass Bright { VertexShader = VS; PixelShader = PS; } pass Dark { VertexShader = VS; PixelShader = Dim; } }
        """;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void EffectsOfTheSameData_OnTwoDevices_MakeThePassShadersOnce()
    {
        var data = Compile(Source);
        var cache = GpuFixture.Main.EffectPassShaders;
        var before = cache.Count;

        using var first = new Effect(GpuFixture.CreateRenderDevice(), data);
        using var second = new Effect(first.GraphicsDevice, data);
        using var elsewhere = new Effect(GpuFixture.CreateRenderDevice(), data);

        Assert.That(cache.Count - before, Is.EqualTo(2), "one for each pass");
    }

    [Test]
    public void ThePassShaders_GoWithTheLastEffectOfTheirData()
    {
        var data = Compile(Source);
        var cache = GpuFixture.Main.EffectPassShaders;
        var before = cache.Count;
        var first = new Effect(GpuFixture.CreateRenderDevice(), data);
        var second = new Effect(GpuFixture.CreateRenderDevice(), data);

        first.Dispose();
        var heldByOne = cache.Count - before;
        second.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(heldByOne, Is.EqualTo(2), "the other effect still holds them");
            Assert.That(cache.Count, Is.EqualTo(before));
        });
    }

    [Test]
    public void EffectsOfDifferentData_HaveShadersOfTheirOwn()
    {
        var cache = GpuFixture.Main.EffectPassShaders;
        var before = cache.Count;

        using var first = new Effect(GpuFixture.CreateRenderDevice(), Compile(Source));
        using var second = new Effect(first.GraphicsDevice, Compile(Source));

        Assert.That(cache.Count - before, Is.EqualTo(4));
    }

    // Hot reload hands every effect of the old data the same new data: they move to its shaders together, and the old
    // ones go once the last effect left them.
    [Test]
    public void EffectsReloadedOnTwoDevices_MoveToTheNewShaders_AndTheOldOnesGo()
    {
        var running = Compile(Source);
        var cache = GpuFixture.Main.EffectPassShaders;
        var before = cache.Count;
        using var first = new Effect(GpuFixture.CreateRenderDevice(), running);
        using var second = new Effect(GpuFixture.CreateRenderDevice(), running);
        var recompiled = Compile(Source.Replace("Tint * 0.5", "Tint * 0.25"));

        Assert.That(first.Reload(recompiled), Is.Null);
        var whileHalfway = cache.Count - before;
        Assert.That(second.Reload(recompiled), Is.Null);

        Assert.Multiple(() =>
        {
            Assert.That(whileHalfway, Is.EqualTo(4), "the second effect still runs the old ones");
            Assert.That(cache.Count - before, Is.EqualTo(2));
        });
    }

    // A pass whose resources descriptor sets cannot push as one set is refused as the effect is made; the passes made
    // before it let go of their shaders.
    [Test]
    public void AnEffectRefusedAsItIsMade_LeavesNoShadersBehind()
    {
        Assume.That(GpuFixture.Main.DescriptorModel, Is.EqualTo(DescriptorModel.Sets),
            "only descriptor sets need a pass's resources in one set");
        var data = Compile("""
            cbuffer Frame { float4 Tint; };
            [[vk::binding(0, 1)]] Texture2D Albedo;
            [[vk::binding(1, 1)]] SamplerState Linear;
            float4 VS(float4 p : POSITION) : SV_Position { return p; }
            float4 PS(float4 p : SV_Position) : SV_Target { return Tint; }
            float4 Textured(float4 p : SV_Position) : SV_Target { return Tint * Albedo.Sample(Linear, p.xy); }
            technique T { pass Plain { VertexShader = VS; PixelShader = PS; } pass Textured { VertexShader = VS; PixelShader = Textured; } }
            """);
        var cache = GpuFixture.Main.EffectPassShaders;
        var before = cache.Count;

        var refusal = Assert.Throws<NotSupportedException>(() => new Effect(GpuFixture.CreateRenderDevice(), data));

        Assert.Multiple(() =>
        {
            Assert.That(refusal.Message, Does.Contain("must share one set"));
            Assert.That(cache.Count, Is.EqualTo(before));
        });
    }

    private static EffectData Compile(string source)
    {
        var compiled = EffectCompiler.Compile(source, "Shared.fx");
        Assert.That(compiled.HasErrors, Is.False, string.Join("\n", compiled.Logger.Messages));
        return compiled.EffectData;
    }
}
