using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Presentation;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.Engine.GraphicsTests;

/// <summary>The Forward+ light assignment on the GPU: which clusters of the view list which lights.</summary>
[TestFixture]
public class ForwardPlusClusterTests
{
    private const int ClusterX = 16;
    private const int ClusterY = 9;
    private const int ClusterZ = 24;
    private const int ClusterCount = ClusterX * ClusterY * ClusterZ;
    private const int MaxLightsPerCluster = 128;
    private const int FloatsPerLight = 16;
    private const float Near = 0.1f;
    private const float Far = 100f;

    [TearDown]
    public void ReleaseDevices() => GpuFixture.ReleaseRenderDevices();

    [Test]
    public void APointLight_IsListedInTheClustersItReaches_AndNoOthers()
    {
        var (counts, lists) = AssignLights(0, PointLight(0, 0, 12, 1));

        var own = Cluster(8, 4, SliceOf(12));
        Assert.That(counts[own], Is.EqualTo(1));
        Assert.That(lists[own * MaxLightsPerCluster], Is.EqualTo(0));
        Assert.That(counts[Cluster(7, 4, SliceOf(12))], Is.EqualTo(1), "the tile across the center it straddles");
        Assert.That(counts[Cluster(0, 4, SliceOf(12))], Is.Zero, "the same depth, far to the left");
        Assert.That(counts[Cluster(8, 0, SliceOf(12))], Is.Zero, "the same depth, far above");
        Assert.That(counts[Cluster(8, 4, SliceOf(2))], Is.Zero, "the same tile, nearer the eye");
        Assert.That(counts[Cluster(8, 4, SliceOf(50))], Is.Zero, "the same tile, farther away");
    }

    [Test]
    public void ALightListsItsOwnIndex_AfterTheOthers()
    {
        var (counts, lists) = AssignLights(0, PointLight(-8, 0, 12, 1), PointLight(0, 0, 12, 1));

        var own = Cluster(8, 4, SliceOf(12));
        Assert.That(counts[own], Is.EqualTo(1));
        Assert.That(lists[own * MaxLightsPerCluster], Is.EqualTo(1));
    }

    [Test]
    public void DirectionalLights_AreNeverListed()
    {
        var (counts, _) = AssignLights(1, PointLight(0, 0, 12, 1));

        Assert.That(counts.Sum(), Is.Zero);
    }

    private static int Cluster(int x, int y, int z) => x + y * ClusterX + z * ClusterX * ClusterY;

    private static int SliceOf(float depth) =>
        (int)((MathF.Log(depth) - MathF.Log(Near)) * ClusterZ / MathF.Log(Far / Near));

    private static float[] PointLight(float x, float y, float z, float range) =>
        [x, y, z, range, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0];

    private static (int[] Counts, int[] Lists) AssignLights(int directional, params float[][] lights)
    {
        var device = GpuFixture.CreateRenderDevice();
        var gd = (GraphicsDevice)device;
        using var effect = Effect.CompileFromFile(Path.Combine("EffectsData", "ForwardPlusEffect.fx"), device);
        var pass = effect.Techniques["ForwardPlus"].Passes["AssignLights"];

        const BufferUsageFlags usage = BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress;
        const MemoryPropertyFlags readable = MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent;
        var packed = lights.SelectMany(light => light).ToArray();
        using var lightBuffer = Adamantium.Graphics.Buffer.New(gd, (ulong)(packed.Length * sizeof(float)), usage, readable);
        using var countBuffer = Adamantium.Graphics.Buffer.New(gd, ClusterCount * sizeof(uint), usage, readable);
        using var listBuffer = Adamantium.Graphics.Buffer.New(gd, ClusterCount * MaxLightsPerCluster * sizeof(uint), usage,
            readable);
        Marshal.Copy(packed, 0, (IntPtr)(nint)lightBuffer.MapMemory(), packed.Length);

        effect.Parameters["Lights"].SetValue(lightBuffer.GetDeviceAddress());
        effect.Parameters["ClusterCounts"].SetValue(countBuffer.GetDeviceAddress());
        effect.Parameters["ClusterLights"].SetValue(listBuffer.GetDeviceAddress());
        effect.Parameters["LightCount"].SetValue((uint)(packed.Length / FloatsPerLight));
        effect.Parameters["DirectionalCount"].SetValue((uint)directional);
        effect.Parameters["ClusterDepth"].SetValue(
            new Vector4F(Near, Far, ClusterZ / MathF.Log(Far / Near), MathF.Log(Near)));
        effect.Parameters["InverseScale"].SetValue(new Vector2F(1, 1));
        // A perspective: w is the depth itself.
        effect.Parameters["ClusterW"].SetValue(new Vector2F(1, 0));
        effect.Parameters["ViewportRect"].SetValue(new Vector4F(0, 0, 160, 90));

        var parameters = new PresentationParameters(PresenterType.RenderTarget, 16, 16, IntPtr.Zero);
        using var presenter = GraphicsPresenter.Create(device, parameters, "forward_plus_clusters");
        device.SetRenderTargets(presenter.RenderTarget);
        device.SetDepthBuffer(presenter.DepthBuffer);
        device.MSAALevel = presenter.MSAALevel;
        device.Presenter = presenter;

        Assert.That(device.BeginDraw(beforeRenderPass: _ =>
        {
            pass.Apply();
            gd.Dispatch((ClusterCount + 63) / 64);
            gd.BufferBarrier(countBuffer,
                PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
            gd.BufferBarrier(listBuffer,
                PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
                PipelineStageFlagBits2.HostBit, AccessFlagBits2.HostReadBit);
        }), Is.True);
        device.EndDraw();
        device.Submit();
        presenter.Present();
        device.FrameEnded();
        device.DeviceWaitIdle();

        var counts = new int[ClusterCount];
        Marshal.Copy((IntPtr)(nint)countBuffer.MapMemory(), counts, 0, counts.Length);
        var lists = new int[ClusterCount * MaxLightsPerCluster];
        Marshal.Copy((IntPtr)(nint)listBuffer.MapMemory(), lists, 0, lists.Length);
        return (counts, lists);
    }
}
