using System;
using System.Linq;
using Adamantium.EffectsCompiler;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>Where a pass's data places its resources for each descriptor model, so every effect of that data binds them
/// alike without working it out again.</summary>
[TestFixture]
public class PassResourcePlacementTests
{
    private const string Source = """
        cbuffer Frame { float4x4 World; };
        Texture2D Albedo;
        Texture2D Layers[3];
        SamplerState Linear;
        StructuredBuffer<float4> Points;

        float4 VS(float4 p : POSITION) : SV_Position { return mul(p, World) + Points[0]; }

        float4 PS(float4 p : SV_Position) : SV_Target
        {
            return Albedo.Sample(Linear, p.xy) + Layers[1].Sample(Linear, p.xy) + World[0];
        }

        technique T { pass P { PixelShader = PS; VertexShader = VS; } }
        """;

    [Test]
    public void TheStages_AreInPipelineOrder_WhateverOrderTheFileNamesThem()
    {
        var pass = Compile(Source);

        Assert.That(pass.Stages.Select(link => link.Stage), Is.EqualTo(new[] { EffectStage.Vertex, EffectStage.Pixel }));
    }

    // Addresses of constant buffers first, eight bytes each; then each stage's heap indices, four bytes an element:
    // images, samplers, buffers.
    [Test]
    public void OnTheHeap_ConstantBuffersComeFirst_ThenTheIndicesStageByStage()
    {
        var pass = Compile(Source);
        var buffer = pass.Resources.Single(resource => resource.Type == EffectParameterType.UniformBuffer);

        Assert.Multiple(() =>
        {
            Assert.That(pass.Resources, Has.Count.EqualTo(5), string.Join(", ", pass.Resources));
            Assert.That(buffer.PushOffset, Is.EqualTo(0));
            Assert.That(OffsetOf(pass, "Points"), Is.EqualTo(8), "the vertex stage's buffer");
            Assert.That(OffsetOf(pass, "Albedo"), Is.EqualTo(12));
            Assert.That(OffsetOf(pass, "Layers"), Is.EqualTo(16));
            Assert.That(OffsetOf(pass, "Linear"), Is.EqualTo(28), "after the three elements of Layers");
            Assert.That(pass.PushDataSize, Is.EqualTo(32));
        });
    }

    [Test]
    public void OnDescriptorSets_TheDescriptorsFollowTheBindings()
    {
        var pass = Compile(Source);
        var byBinding = pass.Resources.OrderBy(resource => resource.Set).ThenBy(resource => resource.Binding).ToList();

        var next = 0;
        foreach (var resource in byBinding)
        {
            Assert.That(resource.Descriptor, Is.EqualTo(next), resource.ToString());
            next += (int)Math.Max(resource.Count, 1);
        }

        Assert.That(pass.DescriptorCount, Is.EqualTo(7));
    }

    [Test]
    public void ATextureArrayOfNoLength_TakesNoPushedDescriptor_AndOneIndexOnTheHeap()
    {
        var pass = Compile("""
            Texture2D Everything[];
            SamplerState Linear;
            float4 VS(float4 p : POSITION) : SV_Position { return p; }
            float4 PS(float4 p : SV_Position) : SV_Target { return Everything[1].Sample(Linear, p.xy); }
            technique T { pass P { VertexShader = VS; PixelShader = PS; } }
            """);
        var array = pass.Resources.Single(resource => resource.Name == "Everything");

        Assert.Multiple(() =>
        {
            Assert.That(array.Count, Is.EqualTo(0));
            Assert.That(array.Descriptor, Is.EqualTo(-1));
            Assert.That(OffsetOf(pass, "Linear"), Is.EqualTo(array.PushOffset + 4));
            Assert.That(pass.DescriptorCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void AResourceTwoStagesRead_IsPlacedOnce()
    {
        var pass = Compile(Source);

        Assert.That(pass.Resources.Count(resource => resource.Name == "Linear"), Is.EqualTo(1));
    }

    [Test]
    public void ThePlacement_SurvivesSavingAndLoading()
    {
        var data = CompileData(Source);
        var stream = new System.IO.MemoryStream();
        data.Save(stream);
        stream.Position = 0;

        var loaded = EffectData.Load(stream).Description.Techniques[0].Passes[0];
        var pass = data.Description.Techniques[0].Passes[0];

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Resources.Select(r => (r.Name, r.Type, r.Set, r.Binding, r.Count, r.PushOffset, r.Descriptor)),
                Is.EqualTo(pass.Resources.Select(r => (r.Name, r.Type, r.Set, r.Binding, r.Count, r.PushOffset, r.Descriptor))));
            Assert.That(loaded.PushDataSize, Is.EqualTo(pass.PushDataSize));
            Assert.That(loaded.DescriptorCount, Is.EqualTo(pass.DescriptorCount));
        });
    }

    // An effect compiled before passes carried their placement would load with none and take the process down as its
    // shaders are made; its format version says so first.
    [Test]
    public void AnEffectOfTheFormatBeforeThePlacement_IsRefused()
    {
        var stream = new System.IO.MemoryStream();
        CompileData(Source).Save(stream);
        var bytes = stream.ToArray();
        BitConverter.GetBytes(3).CopyTo(bytes, 4);

        var refusal = Assert.Throws<System.IO.InvalidDataException>(() => EffectData.Load(bytes));

        Assert.That(refusal.Message, Does.Contain("Rebuild the effects"));
    }

    private static uint OffsetOf(EffectData.Pass pass, string name) =>
        pass.Resources.Single(resource => resource.Name == name).PushOffset;

    private static EffectData.Pass Compile(string source) => CompileData(source).Description.Techniques[0].Passes[0];

    private static EffectData CompileData(string source)
    {
        var result = EffectCompiler.Compile(source, "Placement.fx");
        Assert.That(result.HasErrors, Is.False, string.Join(Environment.NewLine, result.Logger.Messages));
        return result.EffectData;
    }
}
