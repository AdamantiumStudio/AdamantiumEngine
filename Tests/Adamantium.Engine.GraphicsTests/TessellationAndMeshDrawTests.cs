using System;
using System.Runtime.InteropServices;
using Adamantium.EffectsCompiler;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>
/// Tessellated and mesh passes draw: a patch whose control points the caller counts, mesh shaders launched by a task
/// shader or by the draw itself. A draw the device would be handed wrong - a tessellated one of the wrong patch, patches
/// without tessellation, vertices of a mesh pass or mesh tasks of a vertex pass - is refused before it reaches it.
/// </summary>
[TestFixture]
public class TessellationAndMeshDrawTests
{
    private const int Size = 32;

    private const string Source = """
        struct ControlPoint { float4 position : POSITION; };
        struct PatchConstants { float edges[3] : SV_TessFactor; float inside : SV_InsideTessFactor; };
        struct MeshVertex { float4 position : SV_Position; };
        struct TaskPayload { uint count; };

        groupshared TaskPayload payload;

        ControlPoint PatchVS(float2 position : POSITION)
        {
            ControlPoint point;
            point.position = float4(position, 0, 1);
            return point;
        }

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
            vertices[0].position = float4(-1, -1, 0, 1);
            vertices[1].position = float4(3, -1, 0, 1);
            vertices[2].position = float4(-1, 3, 0, 1);
            triangles[0] = uint3(0, 1, 2);
        }

        float4 PS() : SV_Target { return float4(0, 1, 0, 1); }

        float4 PlainVS(float2 position : POSITION) : SV_Position { return float4(position, 0, 1); }

        technique Plain { pass { VertexShader = PlainVS; PixelShader = PS; } }
        technique Tessellated { pass { VertexShader = PatchVS; HullShader = HS; DomainShader = DS; PixelShader = PS; } }
        technique Tasked { pass { TaskShader = TS; MeshShader = MS; PixelShader = PS; } }
        technique Meshed { pass { MeshShader = MS; PixelShader = PS; } }
        """;

    private struct FlatVertex
    {
        [VertexInputElement("POSITION")] public Vector2F Position;
    }

    // A triangle over the whole target, so its middle is drawn wherever the tessellator splits it.
    private static readonly FlatVertex[] Patch =
    [
        new() { Position = new Vector2F(-1, -1) },
        new() { Position = new Vector2F(3, -1) },
        new() { Position = new Vector2F(-1, 3) }
    ];

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void APatchOfTheCallersControlPoints_IsTessellatedAndDrawn()
    {
        var device = CreateDevice();
        Assume.That(device.MainDevice.IsTessellationEnabled, Is.True, "the device has no tessellationShader");
        using var vertices = Adamantium.Graphics.Buffer.Vertex.New(device, Patch);

        var middle = DrawMiddle(device, "Tessellated", () =>
        {
            device.VertexType = typeof(FlatVertex);
            device.PrimitiveTopology = PrimitiveTopology.PatchList;
            device.PatchControlPoints = 3;
            device.Draw(vertices, (uint)Patch.Length);
        });

        Assert.That(middle, Is.True);
    }

    [TestCase(PrimitiveTopology.TriangleList, 3u, "PatchList")]
    [TestCase(PrimitiveTopology.PatchList, 0u, "PatchControlPoints")]
    [TestCase(PrimitiveTopology.PatchList, 4u, "reads patches of 3")]
    public void ATessellatedDrawHandedWrong_IsRefusedBeforeTheDevice(PrimitiveTopology topology, uint points,
        string named)
    {
        var device = CreateDevice();
        Assume.That(device.MainDevice.IsTessellationEnabled, Is.True, "the device has no tessellationShader");
        using var vertices = Adamantium.Graphics.Buffer.Vertex.New(device, Patch);
        InvalidOperationException refusal = null;

        var middle = DrawMiddle(device, "Tessellated", () =>
        {
            device.VertexType = typeof(FlatVertex);
            device.PrimitiveTopology = topology;
            device.PatchControlPoints = points;
            refusal = Assert.Throws<InvalidOperationException>(() => device.Draw(vertices, (uint)Patch.Length));
        });

        Assert.Multiple(() =>
        {
            Assert.That(refusal.Message, Does.Contain(named));
            Assert.That(middle, Is.False, "nothing was drawn");
        });
    }

    [Test]
    public void PatchesWithoutATessellatedPass_AreRefusedBeforeTheDevice()
    {
        var device = CreateDevice();
        using var vertices = Adamantium.Graphics.Buffer.Vertex.New(device, Patch);
        InvalidOperationException refusal = null;

        DrawMiddle(device, "Plain", () =>
        {
            device.VertexType = typeof(FlatVertex);
            device.PrimitiveTopology = PrimitiveTopology.PatchList;
            device.PatchControlPoints = 3;
            refusal = Assert.Throws<InvalidOperationException>(() => device.Draw(vertices, (uint)Patch.Length));
        });

        Assert.That(refusal.Message, Does.Contain("does not tessellate"));
    }

    [Test]
    public void MeshTasksOfAPassWithoutAMeshShader_AreRefused()
    {
        var device = CreateDevice();
        InvalidOperationException refusal = null;

        DrawMiddle(device, "Plain",
            () => refusal = Assert.Throws<InvalidOperationException>(() => device.DrawMeshTasks(1)));

        Assert.That(refusal.Message, Does.Contain("no mesh shader"));
    }

    [Test]
    public void VerticesOfAMeshPass_AreRefused()
    {
        var device = CreateDevice();
        Assume.That(device.MainDevice.IsMeshShaderEnabled, Is.True, "the device has no mesh shaders");
        InvalidOperationException refusal = null;

        DrawMiddle(device, "Meshed",
            () => refusal = Assert.Throws<InvalidOperationException>(() => device.Draw(3, 1)));

        Assert.That(refusal.Message, Does.Contain("is a mesh pass"));
    }

    [Test]
    public void MeshShadersLaunchedByATaskShader_Draw()
    {
        var device = CreateDevice();
        Assume.That(device.MainDevice.IsTaskShaderEnabled, Is.True, "the device has no task shaders");

        Assert.That(DrawMiddle(device, "Tasked", () => device.DrawMeshTasks(1)), Is.True);
    }

    [Test]
    public void AMeshShaderNoTaskShaderLaunches_Draws()
    {
        var device = CreateDevice();
        Assume.That(device.MainDevice.IsMeshShaderEnabled, Is.True, "the device has no mesh shaders");

        Assert.That(DrawMiddle(device, "Meshed", () => device.DrawMeshTasks(1)), Is.True);
    }

    // Mesh shaders take no topology: a PatchList a tessellated draw left set is no reason to refuse them.
    [Test]
    public void AMeshPass_DrawsWhateverTopologyIsLeftSet()
    {
        var device = CreateDevice();
        Assume.That(device.MainDevice.IsMeshShaderEnabled, Is.True, "the device has no mesh shaders");

        Assert.That(DrawMiddle(device, "Meshed", () =>
        {
            device.PrimitiveTopology = PrimitiveTopology.PatchList;
            device.DrawMeshTasks(1);
        }), Is.True);
    }

    [Test]
    public void MoreMeshWorkgroupsThanTheDeviceLaunches_AreRefused()
    {
        var device = CreateDevice();
        Assume.That(device.MainDevice.IsMeshShaderEnabled, Is.True, "the device has no mesh shaders");
        var most = device.Adapter.MeshShaderProperties.MaxMeshWorkGroupCount.Span[0];

        DrawMiddle(device, "Meshed", () =>
            Assert.Throws<ArgumentOutOfRangeException>(() => device.DrawMeshTasks(most + 1)));
    }

    private static GraphicsDevice CreateDevice() => (GraphicsDevice)GpuFixture.CreateRenderDevice();

    // Whether the middle of the target came out green.
    private static bool DrawMiddle(GraphicsDevice device, string technique, Action draw)
    {
        var compiled = EffectCompiler.Compile(Source, "Stages.fx");
        Assert.That(compiled.HasErrors, Is.False, string.Join("\n", compiled.Logger.Messages));
        using var effect = new Effect(device, compiled.EffectData);

        var parameters = new PresentationParameters(PresenterType.RenderTarget, Size, Size, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, parameters, "tessellation_and_mesh");
        var target = (Texture)presenter.RenderTarget.ResolveTexture;
        device.ClearColor = Colors.Black;
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.MSAALevel = presenter.MSAALevel;
        device.Presenter = presenter;
        device.ColorBlendEnabled = false;

        Assert.That(device.BeginDraw(), Is.True);
        device.SetViewports(new Viewport { Width = Size, Height = Size, MinDepth = 0, MaxDepth = 1 });
        device.SetScissors(new Rect2D { Extent = new Extent2D { Width = Size, Height = Size } });
        effect.Techniques[technique].Passes[0].Apply();
        draw();
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        // BGRA, so green is byte 1.
        using var image = target.ToImage();
        var pixel = new byte[4];
        Marshal.Copy(image.DataPointer + (Size / 2 * Size + Size / 2) * 4, pixel, 0, 4);
        return pixel[1] > 128;
    }
}
