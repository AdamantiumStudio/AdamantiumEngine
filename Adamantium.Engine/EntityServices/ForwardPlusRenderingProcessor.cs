using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Managers;
using Adamantium.Engine.Rendering;
using Adamantium.FX;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.EffectsFramework;
using Adamantium.Graphics.Core.Models;
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
    private ForwardPlusWireEffect wireEffect;
    private readonly Stack<(Entity Entity, EditorDisplay Display)> walk = new();
    private BasicEffect basicEffect;
    private MeshGeometryCache geometryCache;
    private bool prepared;
    private readonly Dictionary<MeshDrawKey, MeshDrawGroup> groups = new();
    private readonly List<MeshDrawGroup> groupsDrawn = [];
    private readonly Stack<MeshDrawGroup> spareGroups = new();
    private readonly Dictionary<(Skeleton Skeleton, Matrix4x4F Mesh), (uint First, uint Count)> framePalettes = new();
    private MeshInstance[] instances = new MeshInstance[256];
    private Matrix4x4F[] palettes = new Matrix4x4F[MaxJoints * 4];
    private int paletteMatrices;
    private Buffer[] instanceBuffers = [];
    private Buffer[] paletteBuffers = [];
    private readonly List<(MeshData Data, Matrix4x4F World, bool OverShaded)> wires = [];
    private readonly List<(Entity Entity, Collider Collider)> colliders = [];
    private long trianglesCounted;
    private long trianglesInScene;
    private SceneIndex sceneIndex;
    private Vector3F cameraPosition;

    /// <summary>The most lights one frame takes, directional ones included; the rest are left out.</summary>
    public const int MaxLights = 1024;

    /// <summary>The most joints a skinned mesh is drawn with; a skin with more bends with the first of them.</summary>
    public const int MaxJoints = SceneData.Controller.MaxDrawnJoints;

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

    /// <summary>The triangles of the meshes the last frame drew, those outside the view left out.</summary>
    public long TrianglesDrawn { get; private set; }

    /// <summary>The triangles of every mesh the last frame would draw were it all in view: what
    /// <see cref="TrianglesDrawn"/> is out of.</summary>
    public long TrianglesInScene { get; private set; }

    /// <summary>The draw calls the last frame made for the meshes, their wireframes and colliders: copies of a mesh that
    /// share its pass, texture and state go in one.</summary>
    public int DrawCalls { get; private set; }

    /// <summary>How the whole view draws meshes: shaded, as wireframes, or both.</summary>
    public ViewDisplay Display { get; set; }

    /// <summary>Whether an entity's <see cref="EditorDisplay"/> - set for its subtree - changes how it is drawn, as in an
    /// editor; a game leaves it off and draws the scene as it is.</summary>
    public bool FollowsEditorDisplay { get; set; }

    /// <summary>Whether every collider is drawn over what it holds, as each one's own
    /// <see cref="Collider.DisplayCollider"/> draws it.</summary>
    public bool ShowColliders { get; set; }

    /// <summary>The color wireframes are drawn in.</summary>
    public Vector3F WireColor { get; set; } = new(0.55f, 0.65f, 0.8f);

    /// <summary>Whether a mesh is shaded and whether its wireframe is drawn, the view drawn as <paramref name="view"/>
    /// and the entity set to <paramref name="entity"/>: anything but <see cref="EntityDisplay.AsView"/> takes the
    /// view's place, either way.</summary>
    public static (bool Shaded, bool Wired) Drawn(ViewDisplay view, EntityDisplay entity)
    {
        return entity switch
        {
            EntityDisplay.Shaded => (true, false),
            EntityDisplay.Wireframe => (false, true),
            EntityDisplay.ShadedWireframe => (true, true),
            EntityDisplay.Collider => (false, false),
            _ => (view != ViewDisplay.Wireframe, view != ViewDisplay.Shaded),
        };
    }

    /// <summary>The width in pixels of a wireframe drawn over a shaded mesh. Without VK_KHR_fragment_shader_barycentric
    /// the wireframe is drawn as lines of one pixel.</summary>
    public float WireWidth { get; set; } = 1f;

    protected override void CreateDeviceResources()
    {
        ReleaseDeviceResources();
        basicEffect = new BasicEffect(GraphicsDevice);
        effect = new ForwardPlusEffect(GraphicsDevice);
        if (GraphicsDevice.MainDevice.FragmentBarycentricSupported)
        {
            wireEffect = new ForwardPlusWireEffect(GraphicsDevice);
        }

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

        instanceBuffers = new Buffer[frames];
        paletteBuffers = new Buffer[frames];
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
        SetFrameParameters(effect, camera, frame, directional);
        if (wireEffect != null)
        {
            SetFrameParameters(wireEffect, camera, frame, directional);
            wireEffect.WireColor.SetValue(WireColor);
            wireEffect.WireWidth.SetValue(WireWidth);
        }

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
        wireEffect?.View.SetValue(ActiveCamera.ViewMatrix);
        wireEffect?.ViewProjection.SetValue(ActiveCamera.ViewProjectionMatrix);
        trianglesCounted = 0;
        trianglesInScene = 0;
        sceneIndex = EntityWorld.Satellites.TryGet<SceneIndex>(out var index) ? index : null;
        cameraPosition = (Vector3F)ActiveCamera.WorldPosition;
        try
        {
            foreach (var entity in Entities)
            {
                OnDraw(entity);
            }

            TrianglesDrawn = trianglesCounted;
            TrianglesInScene = trianglesInScene;
            DrawCalls = groupsDrawn.Count + wires.Count + colliders.Count;
            DrawGroups();
            DrawLines();
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "Drawing the scene's meshes failed");
        }
        finally
        {
            ForgetFrame();
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

    private void SetFrameParameters(Effect target, Camera camera, int frame, int directional)
    {
        var near = Math.Max(camera.ZNear, 1e-3f);
        var far = Math.Max(Math.Min(camera.ZFar, ClusterFar), near * 2);
        var projection = camera.ProjectionMatrix;
        var viewport = Window.Viewport;
        var parameters = target.Parameters;

        parameters["LightsAddress"].SetValue(lightBuffers[frame].GetDeviceAddress());
        parameters["ClusterCountsAddress"].SetValue(clusterCounts[frame].GetDeviceAddress());
        parameters["ClusterLightsAddress"].SetValue(clusterLists[frame].GetDeviceAddress());
        parameters["LightCount"].SetValue((uint)LightCount);
        parameters["DirectionalCount"].SetValue((uint)directional);
        parameters["ClusterDepth"].SetValue(new Vector4F(near, far, ClusterSlices / MathF.Log(far / near), MathF.Log(near)));
        parameters["InverseScale"].SetValue(new Vector2F(1f / projection.M11, 1f / projection.M22));
        parameters["ClusterW"].SetValue(new Vector2F(projection.M34, projection.M44));
        parameters["ViewportRect"].SetValue(new Vector4F(viewport.X, viewport.Y, viewport.Width, viewport.Height));
        parameters["Ambient"].SetValue(Ambient);
    }

    private void OnDraw(Entity entity)
    {
        try
        {
            DrawTree(entity);
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "Draw failed for entity {Entity}", entity.Name);
        }
        finally
        {
            walk.Clear();
        }
    }

    private void DrawTree(Entity root)
    {
        walk.Push((root, null));
        while (walk.Count > 0)
        {
            var (entity, display) = walk.Pop();
            if (FollowsEditorDisplay && entity.GetComponent<EditorDisplay>() is { IsEnabled: true } own)
            {
                display = own;
            }

            if (!DrawEntity(entity, display))
            {
                continue;
            }

            foreach (var child in entity.Dependencies.AsSpan())
            {
                if (child != null) walk.Push((child, display));
            }
        }
    }

    private bool DrawEntity(Entity entity, EditorDisplay display)
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

        var shown = display?.Display ?? EntityDisplay.AsView;
        var inView = InView(entity, collider);
        if (inView && collider.ContainsDataFor(ActiveCamera)
            && (collider.DisplayCollider || ShowColliders || shown == EntityDisplay.Collider))
        {
            colliders.Add((entity, collider));
        }

        var meshData = entity.GetComponent<MeshData>();
        if (meshData?.Mesh == null || !meshData.IsEnabled || shown == EntityDisplay.Collider)
        {
            return true;
        }

        var triangles = TriangleCount(meshData);
        trianglesInScene += triangles;
        if (!inView)
        {
            return true;
        }

        var transformation = entity.Transform.GetMetadata(ActiveCamera);
        if (!transformation.Enabled)
        {
            return true;
        }

        var (shaded, wired) = Drawn(Display, shown);
        if (meshData.RenderMode == MeshRenderMode.Skinned)
        {
            shaded = true;
            wired = false;
        }

        var wireInShading = shaded && wired && wireEffect != null && IsTriangles(meshData);
        trianglesCounted += triangles;
        if (shaded)
        {
            Enqueue(entity, meshData, transformation.WorldMatrixF, wireInShading);
        }

        if (wired && !wireInShading)
        {
            wires.Add((meshData, transformation.WorldMatrixF, shaded));
        }

        return true;
    }

    private bool InView(Entity entity, Collider collider)
    {
        var frustum = ActiveCamera.Frustum;
        if (frustum == null)
        {
            return true;
        }

        if (sceneIndex != null && sceneIndex.TryGetBox(entity, out var box))
        {
            return frustum.Contains(new BoundingBox(box.Minimum - cameraPosition, box.Maximum - cameraPosition))
                   != ContainmentType.Disjoint;
        }

        return !collider.ContainsDataFor(ActiveCamera) || entity.ContainsComponent<Skin>()
               || collider.IsInsideCameraFrustum(ActiveCamera) != ContainmentType.Disjoint;
    }

    private static bool IsTriangles(MeshData meshData) =>
        (meshData.TopologyOverride ?? meshData.Mesh.MeshTopology) is PrimitiveTopology.TriangleList
            or PrimitiveTopology.TriangleStrip or PrimitiveTopology.TriangleFan;

    private static long TriangleCount(MeshData meshData)
    {
        var mesh = meshData.Mesh;
        long corners = mesh.HasIndices ? mesh.Indices.Length : mesh.Points?.Length ?? 0;
        return (meshData.TopologyOverride ?? mesh.MeshTopology) switch
        {
            PrimitiveTopology.TriangleList => corners / 3,
            PrimitiveTopology.TriangleStrip or PrimitiveTopology.TriangleFan => Math.Max(corners - 2, 0),
            _ => 0
        };
    }

    private void DrawWire(MeshData meshData, in Matrix4x4F world, bool overShaded)
    {
        basicEffect.Wvp.SetValue(world * ActiveCamera.ViewProjectionMatrix);
        basicEffect.MeshColor.SetValue(WireColor);
        basicEffect.BasicColoredPass.Apply();
        if (overShaded)
        {
            GraphicsDevice.DepthBiasEnabled = true;
            GraphicsDevice.DepthBiasConstantFactor = -1;
            GraphicsDevice.DepthBiasSlopeFactor = -1;
        }

        try
        {
            var state = new RenderState(true, CullModeFlagBits.None, meshData.DepthTestEnabled,
                !overShaded && meshData.DepthWriteEnabled, meshData.TopologyOverride);
            geometryCache.GetOrCreate(meshData.Mesh, typeof(MeshVertex)).Draw(GraphicsDevice, state);
        }
        finally
        {
            GraphicsDevice.DepthBiasEnabled = false;
            GraphicsDevice.DepthBiasConstantFactor = 0;
            GraphicsDevice.DepthBiasSlopeFactor = 0;
            basicEffect.BasicColoredPass.UnApply();
        }
    }

    private void Enqueue(Entity entity, MeshData meshData, in Matrix4x4F world, bool withWire)
    {
        var material = entity.GetComponent<Material>();
        var texture = material?.Texture is { IsDisposed: false } live ? live : null;
        var skinned = meshData.RenderMode == MeshRenderMode.Skinned;
        Effect owner = skinned || !withWire ? effect : wireEffect;
        IEffectPass pass;
        if (skinned)
        {
            pass = texture != null ? effect.ForwardPlusSkinnedTexturedPass : effect.ForwardPlusSkinnedColoredPass;
        }
        else if (withWire)
        {
            pass = texture != null ? wireEffect.ForwardPlusWireTexturedPass : wireEffect.ForwardPlusWireColoredPass;
        }
        else
        {
            pass = texture != null ? effect.ForwardPlusTexturedPass : effect.ForwardPlusColoredPass;
        }

        var geometry = geometryCache.GetOrCreate(meshData.Mesh, meshData.ResolveVertexType());
        var key = new MeshDrawKey(geometry, pass, meshData.CullMode, meshData.DepthTestEnabled,
            meshData.DepthWriteEnabled, meshData.IsWireFrame, meshData.TopologyOverride);
        if (!groups.TryGetValue(key, out var group))
        {
            group = spareGroups.Count > 0 ? spareGroups.Pop() : new MeshDrawGroup();
            group.Key = key;
            group.Effect = owner;
            groups.Add(key, group);
            groupsDrawn.Add(group);
        }

        var (first, count) = skinned ? PaletteOf(entity.GetComponent<Skin>(), world) : default;
        var textureIndex = texture != null ? ((GraphicsDevice)GraphicsDevice).DescriptorHeapManager.TextureIndex(texture) : 0;
        group.Instances.Add(new MeshInstance(world, AlbedoOf(material), first, count, textureIndex));
    }

    private (uint First, uint Count) PaletteOf(Skin skin, in Matrix4x4F mesh)
    {
        if (skin == null)
        {
            return default;
        }

        if (framePalettes.TryGetValue((skin.Skeleton, mesh), out var known))
        {
            return known;
        }

        if (palettes.Length < paletteMatrices + MaxJoints)
        {
            Array.Resize(ref palettes, Math.Max(palettes.Length * 2, paletteMatrices + MaxJoints));
        }

        var filled = skin.FillPalette(palettes.AsSpan(paletteMatrices, MaxJoints), ActiveCamera);
        var palette = ((uint)paletteMatrices, (uint)filled);
        paletteMatrices += filled;
        framePalettes.Add((skin.Skeleton, mesh), palette);
        return palette;
    }

    private void DrawGroups()
    {
        if (groupsDrawn.Count == 0)
        {
            return;
        }

        var count = 0;
        foreach (var group in groupsDrawn)
        {
            group.First = (uint)count;
            count += group.Instances.Count;
        }

        if (instances.Length < count)
        {
            Array.Resize(ref instances, Math.Max(instances.Length * 2, count));
        }

        foreach (var group in groupsDrawn)
        {
            CollectionsMarshal.AsSpan(group.Instances).CopyTo(instances.AsSpan((int)group.First));
        }

        var frame = (int)(GraphicsDevice.CurrentFrame % (uint)instanceBuffers.Length);
        var instanceAddress = Upload(instanceBuffers, frame, instances.AsSpan(0, count));
        var paletteAddress = Upload(paletteBuffers, frame, palettes.AsSpan(0, Math.Max(paletteMatrices, 1)));
        foreach (var target in (ReadOnlySpan<Effect>)[effect, wireEffect])
        {
            target?.Parameters["InstancesAddress"].SetValue(instanceAddress);
            target?.Parameters["PalettesAddress"].SetValue(paletteAddress);
            target?.Parameters["sampleType"].SetResource(SamplerStates.LinearRepeat);
        }

        foreach (var group in groupsDrawn)
        {
            var key = group.Key;
            group.Effect.Parameters["InstanceBase"].SetValue(group.First);
            key.Pass.Apply();
            key.Geometry.Draw(GraphicsDevice, key.State, (uint)group.Instances.Count);
        }
    }

    private void DrawLines()
    {
        foreach (var (data, world, overShaded) in wires)
        {
            DrawWire(data, world, overShaded);
        }

        foreach (var (entity, collider) in colliders)
        {
            DrawCollider(entity, collider);
        }
    }

    private void ForgetFrame()
    {
        foreach (var group in groupsDrawn)
        {
            group.Instances.Clear();
            group.Key = default;
            group.Effect = null;
            spareGroups.Push(group);
        }

        groupsDrawn.Clear();
        groups.Clear();
        framePalettes.Clear();
        paletteMatrices = 0;
        wires.Clear();
        colliders.Clear();
    }

    private ulong Upload<T>(Buffer[] ring, int frame, ReadOnlySpan<T> data) where T : unmanaged
    {
        var bytes = (ulong)(data.Length * Unsafe.SizeOf<T>());
        var buffer = ring[frame];
        if (buffer == null || buffer.TotalSize < bytes)
        {
            if (buffer != null)
            {
                GraphicsDevice.AddToDeferDisposeQueue(buffer);
            }

            var size = Math.Max(bytes, buffer == null ? bytes : buffer.TotalSize * 2);
            buffer = Buffer.New(GraphicsDevice, size, StorageUsage,
                MemoryPropertyFlags.HostVisible | MemoryPropertyFlags.HostCoherent);
            ring[frame] = buffer;
        }

        buffer.SetData(data);
        return buffer.GetDeviceAddress();
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
        wireEffect?.Dispose();
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

        foreach (var buffer in instanceBuffers)
        {
            buffer?.Dispose();
        }

        foreach (var buffer in paletteBuffers)
        {
            buffer?.Dispose();
        }

        lightBuffers = [];
        clusterCounts = [];
        clusterLists = [];
        instanceBuffers = [];
        paletteBuffers = [];
        geometryCache = null;
        effect = null;
        wireEffect = null;
        basicEffect = null;
    }
}
