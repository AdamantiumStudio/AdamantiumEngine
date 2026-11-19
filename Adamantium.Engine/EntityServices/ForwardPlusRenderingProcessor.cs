using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Adamantium.Core;
using Adamantium.Core.Shared;
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
using Texture = Adamantium.Graphics.Texture;

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
    private Buffer[] clusterCounts = [];
    private Buffer[] clusterLists = [];
    private ForwardPlusEffect effect;
    private ForwardPlusWireEffect wireEffect;
    private readonly Stack<WalkStep> walk = new();
    private BasicEffect basicEffect;
    private MeshGeometryCache geometryCache;
    private bool prepared;
    private readonly Dictionary<MeshDrawKey, MeshDrawGroup> groups = new();
    private readonly List<MeshDrawGroup> groupsDrawn = [];
    private readonly Stack<MeshDrawGroup> spareGroups = new();
    private readonly List<MeshDrawGroup> weightedGroups = [];
    private IRenderTarget weightedSums;
    private IRenderTarget weightedCoverage;
    private readonly IRenderTarget[] weightedTargets = new IRenderTarget[2];
    private readonly IRenderTarget[] frameTargets = new IRenderTarget[1];

    // The weighted sums add up; the coverage is laid over what the layers before covered.
    private static readonly ColorBlendEquationEXT[] WeightedBlends =
        [ColorBlendEquations.Sum, ColorBlendEquations.Premultiplied];

    private const ColorComponentFlagBits AllChannels = ColorComponentFlagBits.RBit | ColorComponentFlagBits.GBit
                                                       | ColorComponentFlagBits.BBit | ColorComponentFlagBits.ABit;
    private readonly Dictionary<(Skeleton Skeleton, Matrix4x4F Mesh), (uint First, uint Count)> framePalettes = new();
    private MeshInstance[] instances = new MeshInstance[256];
    private Matrix4x4F[] palettes = [];
    private int paletteMatrices;
    private readonly List<MeshWire> wires = [];
    private readonly List<MeshWire> wiresOverBlended = [];
    private readonly List<(Entity Entity, Collider Collider)> colliders = [];
    private long trianglesCounted;
    private long trianglesInScene;
    private SceneIndex sceneIndex;
    private Vector3F cameraPosition;

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

    /// <summary>Whether the colliders of what is selected in its scene, and of all under it, are drawn - as an editor
    /// shows the selected entity's collider; a model's root shows its parts'.</summary>
    public bool ShowsSelectedColliders { get; set; }

    /// <summary>The color colliders are drawn in.</summary>
    public Vector3F ColliderColor { get; set; } = new(0.57f, 0.96f, 0.55f);

    /// <summary>The color wireframes are drawn in.</summary>
    public Vector3F WireColor { get; set; } = new(0.55f, 0.65f, 0.8f);

    /// <summary>Whether a mesh is shaded and whether its wireframe is drawn, the view drawn as <paramref name="view"/>
    /// and the entity set to <paramref name="entity"/>: anything but <see cref="EntityDisplay.AsView"/> or
    /// <see cref="EntityDisplay.Inherited"/> takes the view's place, either way.</summary>
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
        clusterCounts = new Buffer[frames];
        clusterLists = new Buffer[frames];
        for (int i = 0; i < frames; i++)
        {
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

        var frame = (int)(GraphicsDevice.CurrentFrame % (uint)clusterCounts.Length);
        var directional = CollectLights(camera);
        var lights = GraphicsDevice.UploadFrameData<ClusterLight>(frameLights.AsSpan(0, LightCount));
        SetFrameParameters(effect, camera, frame, directional, lights);
        if (wireEffect != null)
        {
            SetFrameParameters(wireEffect, camera, frame, directional, lights);
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
        geometryCache?.ReleaseUnused();
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
        sceneIndex = Scene?.Index;
        cameraPosition = ActiveCamera.WorldPosition.As<float>();
        try
        {
            foreach (var entity in Entities)
            {
                OnDraw(entity);
            }

            TrianglesDrawn = trianglesCounted;
            TrianglesInScene = trianglesInScene;
            var paletteAddress = GraphicsDevice.UploadFrameData<Matrix4x4F>(palettes.AsSpan(0, paletteMatrices));
            DrawCalls = groupsDrawn.Count + wires.Count + wiresOverBlended.Count + colliders.Count;
            UploadInstances(paletteAddress);
            DrawGroups(groupsDrawn);
            // The lines go with the opaque surfaces: what is see-through over them dims them as it does those. A
            // see-through surface's own wire goes over it.
            DrawLines(paletteAddress);
            DrawCalls += DrawSeeThrough();
            DrawWires(wiresOverBlended, paletteAddress);
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
        // The lights of the scene the output shows; a scene without them is lit by none.
        if (Scene is not { } scene || !scene.Satellites.TryGet<LightManager>(out var lightManager))
        {
            LightCount = 0;
            return 0;
        }

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

    private void SetFrameParameters(Effect target, Camera camera, int frame, int directional, ulong lights)
    {
        var near = Math.Max(camera.ZNear, 1e-3f);
        var far = Math.Max(Math.Min(camera.ZFar, ClusterFar), near * 2);
        var projection = camera.ProjectionMatrix;
        var viewport = Window.Viewport;
        var parameters = target.Parameters;

        parameters["Lights"].SetValue(lights);
        parameters["ClusterCounts"].SetValue(clusterCounts[frame].GetDeviceAddress());
        parameters["ClusterLights"].SetValue(clusterLists[frame].GetDeviceAddress());
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
        walk.Push(new WalkStep(root, null, false));
        while (walk.Count > 0)
        {
            var step = walk.Pop();
            var entity = step.Entity;
            var display = step.Display;
            if (FollowsEditorDisplay
                && entity.GetComponent<EditorDisplay>() is { IsEnabled: true, Display: not EntityDisplay.Inherited } own)
            {
                display = own;
            }

            // Carried down the walk: an entity selected, or one it stands under.
            var underSelected = ShowsSelectedColliders && (step.UnderSelected || entity.IsSelected);
            if (!DrawEntity(entity, display, underSelected))
            {
                continue;
            }

            foreach (var child in entity.Dependencies.AsSpan())
            {
                if (child != null) walk.Push(new WalkStep(child, display, underSelected));
            }
        }
    }

    // An entity still to draw, with the display it is drawn by and whether it is selected or stands under what is.
    private readonly record struct WalkStep(Entity Entity, EditorDisplay Display, bool UnderSelected);

    private bool DrawEntity(Entity entity, EditorDisplay display, bool underSelected)
    {
        if (!entity.Visible)
        {
            return false;
        }

        // Colliders are for the physics only: a mesh without one is still drawn, culled by its box in the scene's index.
        var collider = entity.GetComponent<Collider>();
        var meshData = entity.GetComponent<MeshData>();
        var shown = display?.Display ?? EntityDisplay.AsView;
        var drawsMesh = meshData is { Mesh: not null, IsEnabled: true } && shown != EntityDisplay.Collider;
        if (collider == null && !drawsMesh)
        {
            return true;
        }

        var inView = InView(entity, collider);
        if (collider != null && inView && collider.ContainsDataFor(ActiveCamera)
            && (collider.DisplayCollider || ShowColliders || shown == EntityDisplay.Collider
                || underSelected && collider.IsEnabled))
        {
            colliders.Add((entity, collider));
        }

        if (!drawsMesh)
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
        var material = entity.GetComponent<Material>();
        shaded &= !IsMaskedOut(material);
        if (!shaded && !wired)
        {
            return true;
        }

        // A blended surface's wire is drawn as lines after it, as on a GPU without barycentrics, not faded with it.
        var blended = shaded && material?.AlphaMode == AlphaMode.Blend;
        var wireInShading = shaded && wired && wireEffect != null && IsTriangles(meshData) && !blended;
        trianglesCounted += triangles;
        if (shaded)
        {
            Enqueue(entity, meshData, material, transformation.WorldMatrixF, wireInShading);
        }

        if (wired && !wireInShading)
        {
            var world = transformation.WorldMatrixF;
            var (first, count) = meshData.RenderMode == MeshRenderMode.Skinned
                ? PaletteOf(entity.GetComponent<Skin>(), world)
                : default;
            (blended ? wiresOverBlended : wires).Add(new MeshWire(meshData, world, shaded, first, count));
        }

        return true;
    }

    private bool InView(Entity entity, Collider collider)
    {
        var frustum = ActiveCamera.Frustum;
        if (frustum == default)
        {
            return true;
        }

        if (sceneIndex != null && sceneIndex.TryGetBox(entity, out var box))
        {
            return frustum.Contains(new BoundingBox<float>(box.Minimum - cameraPosition, box.Maximum - cameraPosition))
                   != ContainmentType.Disjoint;
        }

        // A mesh the index leaves out - one on the view's own camera - has no place for this view; with no index, every
        // mesh is.
        if (collider == null)
        {
            return sceneIndex == null;
        }

        return !collider.ContainsDataFor(ActiveCamera) || entity.ContainsComponent<Skin>()
               || collider.IsInsideCameraFrustum(ActiveCamera) != ContainmentType.Disjoint;
    }

    // A masked color without a texture is the same alpha all over: below the cutoff, none of it is drawn.
    private static bool IsMaskedOut(Material material)
    {
        return material is { IsMasked: true } && LiveTextureOf(material) == null
               && material.DiffuseColor.W < material.AlphaCutoff;
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

    // A skinned mesh bends with its joints first, so its lines are drawn by the skinned pass, one copy with its palette.
    private void DrawWire(in MeshWire wire, ulong paletteAddress)
    {
        var meshData = wire.Data;
        var overShaded = wire.OverShaded;
        var skinned = meshData.RenderMode == MeshRenderMode.Skinned;
        if (skinned)
        {
            basicEffect.ViewProjection.SetValue(ActiveCamera.ViewProjectionMatrix);
            basicEffect.View.SetValue(ActiveCamera.ViewMatrix);
            basicEffect.Joints.SetValue(paletteAddress + wire.PaletteFirst * (ulong)Unsafe.SizeOf<Matrix4x4F>());
            basicEffect.JointCount.SetValue(wire.PaletteCount);
            GizmoInstance[] copy = [new GizmoInstance(wire.World, new Vector4F(WireColor, 1f))];
            basicEffect.Instances.SetValue(GraphicsDevice.UploadFrameData<GizmoInstance>(copy));
            basicEffect.BasicSkinnedFlatInstancedPass.Apply();
        }
        else
        {
            basicEffect.Wvp.SetValue(wire.World * ActiveCamera.ViewProjectionMatrix);
            basicEffect.MeshColor.SetValue(WireColor);
            basicEffect.BasicColoredPass.Apply();
        }

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
            var vertexType = skinned ? meshData.ResolveVertexType() : typeof(MeshVertex);
            geometryCache.GetOrCreate(meshData.Mesh, vertexType).Draw(GraphicsDevice, state);
        }
        finally
        {
            GraphicsDevice.DepthBiasEnabled = false;
            GraphicsDevice.DepthBiasConstantFactor = 0;
            GraphicsDevice.DepthBiasSlopeFactor = 0;
        }
    }

    private static Texture LiveTextureOf(Material material) =>
        material?.Texture is { IsDisposed: false } live ? live : null;

    private void Enqueue(Entity entity, MeshData meshData, Material material, in Matrix4x4F world, bool withWire)
    {
        var texture = LiveTextureOf(material);
        var skinned = meshData.RenderMode == MeshRenderMode.Skinned;
        var textured = texture != null;
        var alphaMode = material?.AlphaMode ?? AlphaMode.Opaque;
        var albedo = AlbedoOf(material, alphaMode);
        var alphaCutoff = alphaMode == AlphaMode.Mask ? material.AlphaCutoff : 0f;
        // A see-through surface goes into the weighted layers, in any order, tested against the depth but not writing
        // it; its wire is drawn as lines.
        var seeThrough = alphaMode == AlphaMode.Blend;
        withWire &= !seeThrough;
        var pass = PassOf(withWire, skinned, textured, masked: alphaMode == AlphaMode.Mask, seeThrough);
        var geometry = geometryCache.GetOrCreate(meshData.Mesh, meshData.ResolveVertexType());
        var (first, count) = skinned ? PaletteOf(entity.GetComponent<Skin>(), world) : default;
        var textureIndex = texture != null ? ((GraphicsDevice)GraphicsDevice).DescriptorManager.TextureIndex(texture) : 0;
        var instance = new MeshInstance(world, albedo, first, count, textureIndex, alphaCutoff);

        Effect owner = withWire ? wireEffect : effect;
        var key = new MeshDrawKey(geometry, pass, MeshRenderExtensions.CullModeOf(material, world), meshData.DepthTestEnabled,
            !seeThrough && meshData.DepthWriteEnabled, meshData.IsWireFrame, meshData.TopologyOverride);
        if (!groups.TryGetValue(key, out var group))
        {
            group = spareGroups.Count > 0 ? spareGroups.Pop() : new MeshDrawGroup();
            group.Key = key;
            group.Effect = owner;
            groups.Add(key, group);
            (seeThrough ? weightedGroups : groupsDrawn).Add(group);
        }

        group.Instances.Add(instance);
    }

    // Only a textured surface's alpha changes across it, so only a textured pass is masked: a discard costs the early
    // depth test.
    private IEffectPass PassOf(bool withWire, bool skinned, bool textured, bool masked, bool seeThrough)
    {
        if (seeThrough)
        {
            if (skinned)
                return textured ? effect.ForwardPlusSkinnedWeightedTexturedPass : effect.ForwardPlusSkinnedWeightedColoredPass;
            return textured ? effect.ForwardPlusWeightedTexturedPass : effect.ForwardPlusWeightedColoredPass;
        }

        if (withWire)
        {
            if (!textured)
                return skinned ? wireEffect.ForwardPlusWireSkinnedColoredPass : wireEffect.ForwardPlusWireColoredPass;
            if (masked)
                return skinned ? wireEffect.ForwardPlusWireSkinnedMaskedTexturedPass : wireEffect.ForwardPlusWireMaskedTexturedPass;
            return skinned ? wireEffect.ForwardPlusWireSkinnedTexturedPass : wireEffect.ForwardPlusWireTexturedPass;
        }

        if (!textured)
            return skinned ? effect.ForwardPlusSkinnedColoredPass : effect.ForwardPlusColoredPass;
        if (masked)
            return skinned ? effect.ForwardPlusSkinnedMaskedTexturedPass : effect.ForwardPlusMaskedTexturedPass;
        return skinned ? effect.ForwardPlusSkinnedTexturedPass : effect.ForwardPlusTexturedPass;
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

        var joints = skin.Joints.Count;
        if (palettes.Length < paletteMatrices + joints)
        {
            Array.Resize(ref palettes, Math.Max(palettes.Length * 2, paletteMatrices + joints));
        }

        var filled = skin.FillPalette(palettes.AsSpan(paletteMatrices, joints), ActiveCamera);
        var palette = ((uint)paletteMatrices, (uint)filled);
        paletteMatrices += filled;
        framePalettes.Add((skin.Skeleton, mesh), palette);
        return palette;
    }

    // The opaque groups' copies, then the see-through ones.
    private void UploadInstances(ulong paletteAddress)
    {
        var count = PlaceGroups(groupsDrawn, 0);
        count = PlaceGroups(weightedGroups, count);
        if (count == 0)
        {
            return;
        }

        if (instances.Length < count)
        {
            Array.Resize(ref instances, Math.Max(instances.Length * 2, count));
        }

        CopyInstances(groupsDrawn);
        CopyInstances(weightedGroups);
        var instanceAddress = GraphicsDevice.UploadFrameData<MeshInstance>(instances.AsSpan(0, count));
        foreach (var target in (ReadOnlySpan<Effect>)[effect, wireEffect])
        {
            target?.Parameters["Instances"].SetValue(instanceAddress);
            target?.Parameters["Palettes"].SetValue(paletteAddress);
            target?.Parameters["sampleType"].SetResource(SamplerStates.LinearRepeat);
        }
    }

    // Where each group's copies start in the frame's instances, from first on; gives where the next group starts.
    private static int PlaceGroups(List<MeshDrawGroup> placed, int first)
    {
        foreach (var group in placed)
        {
            group.First = (uint)first;
            first += group.Instances.Count;
        }

        return first;
    }

    private void CopyInstances(List<MeshDrawGroup> copied)
    {
        foreach (var group in copied)
        {
            CollectionsMarshal.AsSpan(group.Instances).CopyTo(instances.AsSpan((int)group.First));
        }
    }

    private void DrawGroups(List<MeshDrawGroup> drawn)
    {
        foreach (var group in drawn)
        {
            var key = group.Key;
            group.Effect.Parameters["InstanceBase"].SetValue(group.First);
            key.Pass.Apply();
            key.Geometry.Draw(GraphicsDevice, key.State, (uint)group.Instances.Count);
        }
    }

    // The see-through surfaces, in any order, into the weighted layers - beside the frame, tested against its depth -
    // then their average laid over the frame as much as they cover it. Gives the calls made.
    private int DrawSeeThrough()
    {
        if (weightedGroups.Count == 0)
        {
            return 0;
        }

        var device = GraphicsDevice;
        var frame = device.CurrentRenderTarget;
        var clearColor = device.ClearColor;
        var channels = device.ColorComponentFlags;
        EnsureWeightedLayers(frame, device.MSAALevel);
        device.SuspendRendering();
        var layersOpen = false;
        try
        {
            KeepDepthForNextPass();
            PrepareToDraw(weightedSums);
            PrepareToDraw(weightedCoverage);
            weightedTargets[0] = weightedSums;
            weightedTargets[1] = weightedCoverage;
            device.SetRenderTargets(weightedTargets);
            device.ClearColor = Colors.Transparent;
            // The sums' alpha is the sum of the weights the color is divided by: written whatever the frame's mask.
            device.ColorComponentFlags = AllChannels;
            device.TargetBlendEquations = WeightedBlends;
            device.BeginRendering(device.CurrentCommandBuffer, keepDepth: true);
            layersOpen = true;
            DrawGroups(weightedGroups);
        }
        finally
        {
            // Closed before the frame opens again, even when a draw threw.
            if (layersOpen)
            {
                device.SuspendRendering();
                PrepareToRead(weightedSums);
                PrepareToRead(weightedCoverage);
            }

            KeepDepthForNextPass();
            device.TargetBlendEquations = null;
            device.ColorComponentFlags = channels;
            frameTargets[0] = frame;
            device.SetRenderTargets(frameTargets);
            device.ClearColor = clearColor;
            device.ResumeRendering();
        }

        Composite();
        return weightedGroups.Count;
    }

    // The depth one pass wrote, seen by the next that loads it: passes are not ordered by themselves.
    private void KeepDepthForNextPass()
    {
        if (GraphicsDevice.CurrentDepthStencilBuffer is not { } depth)
            return;

        const PipelineStageFlagBits2 depthTests =
            PipelineStageFlagBits2.EarlyFragmentTestsBit | PipelineStageFlagBits2.LateFragmentTestsBit;
        GraphicsDevice.InsertImageMemoryBarrier(GraphicsDevice.CurrentCommandBuffer, depth,
            AccessFlagBits2.DepthStencilAttachmentWriteBit,
            AccessFlagBits2.DepthStencilAttachmentReadBit | AccessFlagBits2.DepthStencilAttachmentWriteBit,
            ImageLayout.DepthStencilAttachmentOptimal, ImageLayout.DepthStencilAttachmentOptimal, depthTests, depthTests);
    }

    private void Composite()
    {
        var device = GraphicsDevice;
        var descriptors = ((GraphicsDevice)device).DescriptorManager;
        var blend = device.ColorBlendEquation;
        var vertexType = device.VertexType;
        var topology = device.PrimitiveTopology;
        var polygonMode = device.PolygonMode;
        var cullMode = device.CullMode;
        var depthTest = device.DepthTestEnabled;
        var depthWrite = device.DepthWriteEnable;
        effect.Parameters["AccumulatedTexture"].SetValue(descriptors.TextureIndex(weightedSums.ResolveTexture));
        effect.Parameters["CoverageTexture"].SetValue(descriptors.TextureIndex(weightedCoverage.ResolveTexture));
        device.ColorBlendEquation = ColorBlendEquations.AlphaBlend;
        device.VertexType = null;
        device.PrimitiveTopology = PrimitiveTopology.TriangleList;
        device.PolygonMode = PolygonMode.Fill;
        device.CullMode = CullModeFlagBits.None;
        device.DepthTestEnabled = false;
        device.DepthWriteEnable = false;
        try
        {
            effect.ForwardPlusWeightedCompositePass.Apply();
            device.Draw(3, 1);
        }
        finally
        {
            device.ColorBlendEquation = blend;
            device.VertexType = vertexType;
            device.PrimitiveTopology = topology;
            device.PolygonMode = polygonMode;
            device.CullMode = cullMode;
            device.DepthTestEnabled = depthTest;
            device.DepthWriteEnable = depthWrite;
        }
    }

    // The layers the size and samples of the frame they go over: made anew when it changes, the old ones let go once the
    // frames in flight are done with them.
    private void EnsureWeightedLayers(IRenderTarget frame, MSAALevel samples)
    {
        if (weightedSums is { IsDisposed: false } && weightedSums.Width == frame.Width
            && weightedSums.Height == frame.Height && weightedSums.MSAALevel == samples)
        {
            return;
        }

        ReleaseWeightedLayers();
        weightedSums = GraphicsDevice.CreateRenderTarget(frame.Width, frame.Height, samples,
            Format.R16G16B16A16_SFLOAT, ImageUsageFlagBits.SampledBit, ImageLayout.ColorAttachmentOptimal, "See-through sums");
        weightedCoverage = GraphicsDevice.CreateRenderTarget(frame.Width, frame.Height, samples,
            Format.R16_SFLOAT, ImageUsageFlagBits.SampledBit, ImageLayout.ColorAttachmentOptimal, "See-through coverage");
    }

    // Each by the device that made it, which may no longer be the processor's.
    private void ReleaseWeightedLayers()
    {
        foreach (var layer in (ReadOnlySpan<IRenderTarget>)[weightedSums, weightedCoverage])
        {
            if (layer is IDisposable disposable && !layer.IsDisposed)
                layer.GraphicsDevice.AddToDeferDisposeQueue(disposable);
        }

        weightedSums = null;
        weightedCoverage = null;
    }

    // What a layer held last frame - read by its composite - is cleared: whatever layout it was left in goes.
    private void PrepareToDraw(IRenderTarget layer)
    {
        var commands = GraphicsDevice.CurrentCommandBuffer;
        GraphicsDevice.InsertImageMemoryBarrier(commands, layer, AccessFlagBits2.ShaderReadBit,
            AccessFlagBits2.ColorAttachmentWriteBit, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal,
            PipelineStageFlagBits2.FragmentShaderBit | PipelineStageFlagBits2.ColorAttachmentOutputBit,
            PipelineStageFlagBits2.ColorAttachmentOutputBit);
        if (!ReferenceEquals(layer.ResolveTexture, layer))
        {
            GraphicsDevice.InsertImageMemoryBarrier(commands, layer.ResolveTexture, AccessFlagBits2.ShaderReadBit,
                AccessFlagBits2.ColorAttachmentWriteBit, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal,
                PipelineStageFlagBits2.FragmentShaderBit | PipelineStageFlagBits2.ColorAttachmentOutputBit,
                PipelineStageFlagBits2.ColorAttachmentOutputBit);
        }
    }

    private void PrepareToRead(IRenderTarget layer)
    {
        GraphicsDevice.InsertImageMemoryBarrier(GraphicsDevice.CurrentCommandBuffer, layer.ResolveTexture,
            AccessFlagBits2.ColorAttachmentWriteBit, AccessFlagBits2.ShaderReadBit, ImageLayout.ColorAttachmentOptimal,
            ImageLayout.ShaderReadOnlyOptimal, PipelineStageFlagBits2.ColorAttachmentOutputBit,
            PipelineStageFlagBits2.FragmentShaderBit);
    }

    private void DrawWires(List<MeshWire> drawn, ulong paletteAddress)
    {
        foreach (var wire in drawn)
        {
            DrawWire(wire, paletteAddress);
        }
    }

    private void DrawLines(ulong paletteAddress)
    {
        DrawWires(wires, paletteAddress);

        foreach (var (entity, collider) in colliders)
        {
            DrawCollider(entity, collider);
        }
    }

    private void ForgetFrame()
    {
        Recycle(groupsDrawn);
        Recycle(weightedGroups);
        groups.Clear();
        framePalettes.Clear();
        paletteMatrices = 0;
        wires.Clear();
        wiresOverBlended.Clear();
        colliders.Clear();
    }

    private void Recycle(List<MeshDrawGroup> recycled)
    {
        foreach (var group in recycled)
        {
            group.Instances.Clear();
            group.Key = default;
            group.Effect = null;
            spareGroups.Push(group);
        }

        recycled.Clear();
    }

    private void DrawCollider(Entity entity, Collider collider)
    {
        var transform = entity.Transform.GetMetadata(ActiveCamera);
        basicEffect.Wvp.SetValue(transform.WorldMatrixF * ActiveCamera.ViewProjectionMatrix);
        basicEffect.MeshColor.SetValue(ColliderColor);
        basicEffect.BasicColoredPass.Apply();
        if (collider.GetVisualRepresentation() is { } outline)
        {
            geometryCache.GetOrCreate(outline, typeof(MeshVertex)).Draw(GraphicsDevice, RenderState.Default);
        }
    }

    // The diffuse color's alpha counts unless the material is opaque.
    private Vector4F AlbedoOf(Material material, AlphaMode alphaMode)
    {
        if (material == null)
        {
            return new Vector4F(UntexturedColor, 1f);
        }

        var diffuse = material.DiffuseColor;
        var alpha = alphaMode == AlphaMode.Opaque ? 1f : diffuse.W;
        var color = diffuse.X + diffuse.Y + diffuse.Z > 0 ? new Vector3F(diffuse.X, diffuse.Y, diffuse.Z) : UntexturedColor;
        return new Vector4F(color, alpha);
    }

    private void ReleaseDeviceResources()
    {
        ReleaseWeightedLayers();
        geometryCache?.Dispose();
        effect?.Dispose();
        wireEffect?.Dispose();
        basicEffect?.Dispose();
        foreach (var buffer in clusterCounts)
        {
            buffer.Dispose();
        }

        foreach (var buffer in clusterLists)
        {
            buffer.Dispose();
        }

        clusterCounts = [];
        clusterLists = [];
        geometryCache = null;
        effect = null;
        wireEffect = null;
        basicEffect = null;
    }
}
