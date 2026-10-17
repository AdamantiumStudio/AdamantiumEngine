using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Managers;
using Adamantium.Engine.Rendering;
using Adamantium.FX;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using Adamantium.Vulkan.Core;
using Serilog;
using Buffer = Adamantium.Graphics.Buffer;
using GraphicsDevice = Adamantium.Graphics.GraphicsDevice;

namespace Adamantium.Engine.EntityServices;

/// <summary>
/// Draws the scene lit by its lights, clustered: before the frame a compute pass lists the point and spot lights that
/// reach each cluster of the view, and a pixel is shaded with its own cluster's list only. Directional lights reach
/// every pixel. The lights are the universe's <see cref="LightManager"/>'s, which has to be among its satellites.
/// </summary>
public class ForwardPlusRenderingProcessor : RenderingProcessor
{
    private const uint ClusterSlices = 24;
    private const uint ClusterCount = 16 * 9 * ClusterSlices;
    private const uint MaxLightsPerCluster = 128;

    private static readonly BufferUsageFlags StorageUsage =
        BufferUsageFlags.StorageBuffer | BufferUsageFlags.ShaderDeviceAddress;

    private readonly ClusterLight[] frameLights = new ClusterLight[MaxLights];
    private LightManager lightManager;
    private Buffer[] lightBuffers = [];
    private Buffer[] clusterCounts = [];
    private Buffer[] clusterLists = [];
    private ForwardPlusEffect effect;
    private BasicEffect basicEffect;
    private MeshGeometryCache geometryCache;
    private bool prepared;

    /// <summary>The most lights one frame takes, directional ones included; the rest are left out.</summary>
    public const int MaxLights = 1024;

    /// <summary>What the scene is drawn over: black by default, so only the lights show the surfaces.</summary>
    public Color ClearColor { get; set; } = Colors.Black;

    /// <summary>The light every surface gets whatever the lights in the scene.</summary>
    public Vector3F Ambient { get; set; } = new(0.08f, 0.08f, 0.1f);

    /// <summary>The color of a surface that has neither a texture nor a diffuse color.</summary>
    public Vector3F UntexturedColor { get; set; } = new(0.75f, 0.75f, 0.75f);

    /// <summary>How far from the camera the clusters reach; a point or spot light beyond it lights nothing.</summary>
    public float ClusterFar { get; set; } = 500f;

    /// <summary>The lights of the last frame, directional ones included.</summary>
    public int LightCount { get; private set; }

    protected override void CreateDeviceResources()
    {
        ReleaseDeviceResources();
        basicEffect = new BasicEffect(GraphicsDevice);
        effect = new ForwardPlusEffect(GraphicsDevice);
        geometryCache = new MeshGeometryCache(GraphicsDevice);

        var frames = (int)Math.Max(1u, GraphicsDevice.MaxFramesInFlight);
        lightBuffers = new Buffer[frames];
        clusterCounts = new Buffer[frames];
        clusterLists = new Buffer[frames];
        for (int i = 0; i < frames; i++)
        {
            lightBuffers[i] = Buffer.New(GraphicsDevice, (ulong)(MaxLights * Marshal.SizeOf<ClusterLight>()), StorageUsage,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
            clusterCounts[i] = Buffer.New(GraphicsDevice, ClusterCount * sizeof(uint), StorageUsage);
            clusterLists[i] = Buffer.New(GraphicsDevice, ClusterCount * MaxLightsPerCluster * sizeof(uint), StorageUsage);
        }

        base.CreateDeviceResources();
    }

    protected override void OnDetached()
    {
        ReleaseDeviceResources();
        base.OnDetached();
    }

    public override void PreRender()
    {
        prepared = false;
        GraphicsDevice.ClearColor = ClearColor;
        var camera = Window.Camera;
        if (camera == null || effect == null)
        {
            return;
        }

        var frame = (int)(GraphicsDevice.CurrentFrame % (uint)lightBuffers.Length);
        var directional = CollectLights(camera);
        lightBuffers[frame].SetData<ClusterLight>(frameLights.AsSpan(0, LightCount));
        SetFrameParameters(camera, frame, directional);

        var device = (GraphicsDevice)GraphicsDevice;
        effect.ForwardPlusAssignLightsPass.Apply();
        device.Dispatch((ClusterCount + 63) / 64);
        device.BufferBarrier(clusterCounts[frame],
            PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
            PipelineStageFlagBits2.FragmentShaderBit, AccessFlagBits2.ShaderReadBit);
        device.BufferBarrier(clusterLists[frame],
            PipelineStageFlagBits2.ComputeShaderBit, AccessFlagBits2.ShaderWriteBit,
            PipelineStageFlagBits2.FragmentShaderBit, AccessFlagBits2.ShaderReadBit);
        prepared = true;
    }

    public override void Draw(AppTime appTime)
    {
        ActiveCamera = Window.Camera;
        if (ActiveCamera == null || !prepared)
        {
            return;
        }

        effect.View.SetValue(ActiveCamera.ViewMatrix);
        effect.ViewProjection.SetValue(ActiveCamera.ViewProjectionMatrix);
        foreach (var entity in Entities)
        {
            OnDraw(entity);
        }
    }

    private int CollectLights(Camera camera)
    {
        lightManager ??= EntityWorld.Satellites.Get<LightManager>();
        var count = Append(lightManager.DirectionalLights, camera, 0);
        var directional = count;
        count = Append(lightManager.PointLights, camera, count);
        count = Append(lightManager.SpotLights, camera, count);
        LightCount = count;
        return directional;
    }

    private int Append(IReadOnlyList<Light> lights, Camera camera, int count)
    {
        for (int i = 0; i < lights.Count && count < MaxLights; i++)
        {
            var light = lights[i];
            if (light.IsEnabled && light.Owner is { Visible: true })
            {
                frameLights[count++] = light.ToClusterLight(camera);
            }
        }

        return count;
    }

    private void SetFrameParameters(Camera camera, int frame, int directional)
    {
        var near = Math.Max(camera.ZNear, 1e-3f);
        var far = Math.Max(Math.Min(camera.ZFar, ClusterFar), near * 2);
        var projection = camera.ProjectionMatrix;
        var viewport = Window.Viewport;

        effect.LightsAddress.SetValue(lightBuffers[frame].GetDeviceAddress());
        effect.ClusterCountsAddress.SetValue(clusterCounts[frame].GetDeviceAddress());
        effect.ClusterLightsAddress.SetValue(clusterLists[frame].GetDeviceAddress());
        effect.LightCount.SetValue((uint)LightCount);
        effect.DirectionalCount.SetValue((uint)directional);
        effect.ClusterDepth.SetValue(new Vector4F(near, far, ClusterSlices / MathF.Log(far / near), MathF.Log(near)));
        effect.InverseScale.SetValue(new Vector2F(1f / projection.M11, 1f / projection.M22));
        effect.ViewportRect.SetValue(new Vector4F(viewport.X, viewport.Y, viewport.Width, viewport.Height));
        effect.Ambient.SetValue(Ambient);
    }

    private void OnDraw(Entity entity)
    {
        try
        {
            entity.VisitInDepth(DrawEntity);
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "Draw failed for entity {Entity}", entity.Name);
        }
    }

    private bool DrawEntity(Entity entity)
    {
        if (!entity.Visible)
        {
            return false;
        }

        var collider = entity.GetComponent<Collider>();
        if (collider == null)
        {
            return true;
        }

        if (collider.ContainsDataFor(ActiveCamera))
        {
            if (collider.IsInsideCameraFrustum(ActiveCamera) == ContainmentType.Disjoint)
            {
                return true;
            }

            if (collider.DisplayCollider)
            {
                DrawCollider(entity, collider);
            }
        }

        var meshData = entity.GetComponent<MeshData>();
        if (meshData?.Mesh == null || !meshData.IsEnabled)
        {
            return true;
        }

        var transformation = entity.Transform.GetMetadata(ActiveCamera);
        if (!transformation.Enabled)
        {
            return true;
        }

        var material = entity.GetComponent<Material>();
        if (meshData.RenderMode == MeshRenderMode.Skinned)
        {
            basicEffect.Wvp.SetValue(transformation.WorldMatrixF * ActiveCamera.ViewProjectionMatrix);
            basicEffect.Techniques["Basic"].Passes["Skinned"].Apply();
        }
        else
        {
            effect.World.SetValue(transformation.WorldMatrixF);
            if (material?.Texture != null)
            {
                effect.SampleType.SetResource(SamplerStates.LinearRepeat);
                effect.ShaderTexture.SetResource(material.Texture);
                effect.ForwardPlusTexturedPass.Apply();
            }
            else
            {
                effect.Albedo.SetValue(AlbedoOf(material));
                effect.ForwardPlusColoredPass.Apply();
            }
        }

        geometryCache.DrawMesh(GraphicsDevice, meshData);
        return true;
    }

    private void DrawCollider(Entity entity, Collider collider)
    {
        var transform = entity.Transform.GetMetadata(ActiveCamera);
        basicEffect.Wvp.SetValue(transform.WorldMatrixF * ActiveCamera.ViewProjectionMatrix);
        basicEffect.MeshColor.SetValue(Colors.White.ToVector3());
        basicEffect.BasicColoredPass.Apply();
        if (collider.Geometry == null)
        {
            collider.GetVisualRepresentation();
        }

        if (collider.Geometry != null)
        {
            geometryCache.GetOrCreate(collider.Geometry, typeof(MeshVertex)).Draw(GraphicsDevice, RenderState.Default);
        }

        basicEffect.BasicColoredPass.UnApply();
    }

    private Vector3F AlbedoOf(Material material)
    {
        if (material == null)
        {
            return UntexturedColor;
        }

        var diffuse = material.DiffuseColor;
        return diffuse.X + diffuse.Y + diffuse.Z > 0 ? new Vector3F(diffuse.X, diffuse.Y, diffuse.Z) : UntexturedColor;
    }

    private void ReleaseDeviceResources()
    {
        geometryCache?.Dispose();
        effect?.Dispose();
        basicEffect?.Dispose();
        foreach (var buffer in lightBuffers)
        {
            buffer.Dispose();
        }

        foreach (var buffer in clusterCounts)
        {
            buffer.Dispose();
        }

        foreach (var buffer in clusterLists)
        {
            buffer.Dispose();
        }

        lightBuffers = [];
        clusterCounts = [];
        clusterLists = [];
        geometryCache = null;
        effect = null;
        basicEffect = null;
    }
}
