using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Presentation;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>Whether a pass runs is the device's to say: hull and domain shaders need the tessellationShader feature,
/// task and mesh shaders VK_EXT_mesh_shader - each enabled when the device has it - and ray-tracing shaders a
/// ray-tracing pipeline, which effects do not create yet. A pass the device cannot run refuses when it is applied,
/// saying what it needs; the effect's other passes run.</summary>
[TestFixture]
public class EffectStageSupportTests
{
    private const string Source = """
        struct ControlPoint { float4 position : POSITION; };
        struct PatchConstants { float edges[3] : SV_TessFactor; float inside : SV_InsideTessFactor; };
        struct MeshVertex { float4 position : SV_Position; };
        struct TaskPayload { uint count; };
        struct RayPayload { float4 color; };

        cbuffer MeshParameters { float MeshScale; };
        Texture2D MeshTexture;

        groupshared TaskPayload payload;

        float4 VS(float4 position : POSITION) : SV_Position { return position; }

        ControlPoint PatchVS(ControlPoint point) { return point; }

        PatchConstants Constants(InputPatch<ControlPoint, 3> patch)
        {
            PatchConstants constants;
            constants.edges[0] = constants.edges[1] = constants.edges[2] = 4;
            constants.inside = 4;
            return constants;
        }

        [domain("tri")]
        [partitioning("integer")]
        [outputtopology("triangle_cw")]
        [outputcontrolpoints(3)]
        [patchconstantfunc("Constants")]
        ControlPoint HS(InputPatch<ControlPoint, 3> patch, uint id : SV_OutputControlPointID) { return patch[id]; }

        [domain("tri")]
        float4 DS(PatchConstants constants, float3 at : SV_DomainLocation, const OutputPatch<ControlPoint, 3> patch)
            : SV_Position
        {
            return patch[0].position * at.x + patch[1].position * at.y + patch[2].position * at.z;
        }

        [shader("amplification")]
        [numthreads(1, 1, 1)]
        void TS()
        {
            payload.count = 1;
            DispatchMesh(1, 1, 1, payload);
        }

        [shader("mesh")]
        [outputtopology("triangle")]
        [numthreads(1, 1, 1)]
        void MS(out vertices MeshVertex vertices[3], out indices uint3 triangles[1])
        {
            SetMeshOutputCounts(3, 1);
            vertices[0].position = float4(0, 0, 0, MeshScale) + MeshTexture.Load(int3(0, 0, 0));
            vertices[1].position = float4(1, 0, 0, 1);
            vertices[2].position = float4(0, 1, 0, 1);
            triangles[0] = uint3(0, 1, 2);
        }

        float4 PS() : SV_Target { return 1; }

        void RG() {}

        void Miss(inout RayPayload payload) { payload.color = 0; }

        void Hit(inout RayPayload payload, BuiltInTriangleIntersectionAttributes attributes) { payload.color = 1; }

        technique Plain { pass { VertexShader = VS; PixelShader = PS; } }
        technique Reversed { pass { PixelShader = PS; VertexShader = VS; } }
        technique Tessellated { pass { VertexShader = PatchVS; HullShader = HS; DomainShader = DS; PixelShader = PS; } }
        technique Meshed { pass { TaskShader = TS; MeshShader = MS; PixelShader = PS; } }
        technique Traced { pass { RayGenerationShader = RG; MissShader = Miss; HitGroup Triangles { ClosestHit = Hit; } } }
        """;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void APass_RunsWhereTheDeviceHasItsStages_AndElsewhereRefusesWhenApplied()
    {
        var device = (GraphicsDevice)GpuFixture.CreateRenderDevice();
        var compiled = EffectCompiler.Compile(Source, "Stages.fx");
        Assert.That(compiled.HasErrors, Is.False, string.Join("\n", compiled.Logger.Messages));
        using var effect = new Effect(device, compiled.EffectData);

        var prms = new PresentationParameters(PresenterType.RenderTarget, 16, 16, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, prms, "stage_support");
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.MSAALevel = presenter.MSAALevel;
        device.Presenter = presenter;

        var applied = new Dictionary<string, string>();
        Assert.That(device.BeginDraw(beforeRenderPass: _ =>
        {
            foreach (var technique in effect.Techniques)
            {
                try
                {
                    technique.Passes[0].Apply();
                    applied[technique.Name] = "applied";
                }
                catch (NotSupportedException refusal)
                {
                    applied[technique.Name] = refusal.Message;
                }
            }
        }), Is.True);
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        var main = device.MainDevice;
        Assert.Multiple(() =>
        {
            Assert.That(applied["Plain"], Is.EqualTo("applied"));
            Assert.That(applied["Reversed"], Is.EqualTo("applied"), "stages chain in pipeline order, not the file's");
            Assert.That(effect.Parameters["MeshScale"], Is.Not.Null, "a refused pass still declares its parameters");
            Assert.That(effect.Parameters["MeshTexture"], Is.Not.Null);
            Assert.That(applied["Tessellated"], main.IsTessellationEnabled
                ? Is.EqualTo("applied")
                : Does.Contain("tessellationShader"));
            Assert.That(applied["Meshed"], main.IsMeshShaderEnabled && main.IsTaskShaderEnabled
                ? Is.EqualTo("applied")
                : Does.Contain("VK_EXT_mesh_shader"));
            Assert.That(applied["Traced"], Does.Contain("ray-tracing pipeline"));
            Assert.That(applied.Keys.Order(), Is.EqualTo(new[] { "Meshed", "Plain", "Reversed", "Tessellated", "Traced" }));
        });
    }

    // An element set by index lands at that index, the others left as they were.
    [Test]
    public void AResourceSetAtAnIndex_IsBoundAtThatIndex()
    {
        var device = GpuFixture.CreateRenderDevice();
        var compiled = EffectCompiler.Compile("""
            SamplerState Samplers[2];
            Texture2D Image;
            float4 VS(float4 position : POSITION) : SV_Position { return position; }
            float4 PS() : SV_Target { return Image.Sample(Samplers[0], 0) + Image.Sample(Samplers[1], 0); }
            technique T { pass { VertexShader = VS; PixelShader = PS; } }
            """, "Indexed.fx");
        Assert.That(compiled.HasErrors, Is.False, string.Join("\n", compiled.Logger.Messages));
        using var effect = new Effect(device, compiled.EffectData);
        var samplers = effect.Parameters["Samplers"];

        samplers.SetResource(1, SamplerStates.LinearClampToEdge);

        Assert.That(effect.ResourceLinker.GetBoundValues(samplers.ParameterDescription),
            Is.EqualTo(new object[] { null, SamplerStates.LinearClampToEdge }));
    }
}
