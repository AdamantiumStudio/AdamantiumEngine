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
    private IRenderTarget sceneLight;
    private IRenderTarget[] glowCopies = [];
    private readonly IRenderTarget[] sceneLightTargets = new IRenderTarget[1];
    private readonly IRenderTarget[] pictureTargets = new IRenderTarget[1];
    private IRenderTarget skyIrradiance;
    private IRenderTarget[] skyReflections = [];
    private SkyKind? skyMade;
    private SkyKind sky;
    private float skyStrength;

    // The sky's pictures: the light round a normal small, the mirrored sky from this wide, each rougher one half the one
    // before.
    private const uint SkyIrradianceWidth = 64;
    private const uint SkyReflectionWidth = 256;
    private const int SkyReflectionLevels = 6;
    private RenderingService attachedTo;
    private SceneBackgroundProcessor background;
    private bool linearFrame;
    private float exposure;
    private float glow;
    private IDepthStencilBuffer shadowMap;
    private Light shadowCaster;
    private int shadowLight = -1;
    private readonly Matrix4x4F[] cascadeViews = new Matrix4x4F[Cascades];
    private readonly Matrix4x4F[] cascadeLookups = new Matrix4x4F[Cascades];
    private readonly IRenderTarget[] noTargets = [];
    private uint cascadeSide;
    private readonly BoundingFrustum<float>[] cascadeReaches = new BoundingFrustum<float>[Cascades];
    private readonly List<MeshDrawGroup> casterGroups = [];
    private readonly float[] cascadeEnds = new float[Cascades];
    private readonly float[] cascadeTexels = new float[Cascades];

    // The shadow's cascades, each a quarter of its map; how much of a cascade's reach follows the near ones' spacing.
    private const int Cascades = 4;
    private const float CascadeSpacing = 0.75f;
    private const int DefaultCascadeSide = 1024;
    private const int LargestCascadeSide = 4096;
    private ColorComponentFlagBits frameChannels;
    private Color frameClearColor;

    // The glow's copies stop when the smaller side reaches this, or at so many.
    private const uint SmallestGlow = 8;
    private const int MostGlowCopies = 7;

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
    private WorldPosition cameraPosition;

    /// <summary>The most lights one frame takes, directional ones included; the rest are left out.</summary>
    public const int MaxLights = 1024;

    /// <summary>What the scene is drawn over: black by default, so only the lights show the surfaces.</summary>
    public Color ClearColor { get; set; } = Colors.Black;

    /// <summary>The light every surface gets whatever the lights in the scene.</summary>
    public Vector3F Ambient { get; set; } = new(0.08f, 0.08f, 0.1f);

    /// <summary>How far from the camera the shadow of a directional light reaches. The first directional light whose
    /// <see cref="Light.IsWithShadows"/> is on casts it, in four cascades, each <see cref="Light.ShadowMapResolution"/>
    /// texels square (1024 when it is not set).</summary>
    public float ShadowDistance { get; set; } = 100f;

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

    protected override void OnAttached()
    {
        base.OnAttached();
        attachedTo = AssociatedService;
        background = new SceneBackgroundProcessor(this);
        attachedTo.AttachProcessor(background);
    }

    protected override void OnDetached()
    {
        attachedTo?.DetachProcessor(background);
        background = null;
        attachedTo = null;
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
        // Held for the frame: the shaders were told it here, the draw must not go otherwise.
        var environment = Scene?.Environment;
        linearFrame = environment?.HighDynamicRange == true;
        exposure = environment?.Exposure ?? 0;
        glow = environment?.Glow ?? 0;
        sky = environment?.Sky ?? SkyKind.None;
        skyStrength = environment?.SkyStrength ?? 1;
        if (sky != SkyKind.None && skyMade != sky)
        {
            MakeSky();
        }
        var directional = CollectLights(camera);
        if (shadowCaster != null)
        {
            EnsureShadowMap(shadowCaster.ShadowMapResolution);
            PlaceCascades(camera);
        }

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
        cameraPosition = ActiveCamera.WorldPosition;
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
            if (shadowCaster != null)
            {
                DrawShadow();
            }

            var frame = linearFrame ? OpenSceneLight() : null;
            try
            {
                DrawGroups(groupsDrawn);
                // The lines go with the opaque surfaces: what is see-through over them dims them as it does those. A
                // see-through surface's own wire goes over it.
                DrawLines(paletteAddress);
                DrawCalls += DrawSeeThrough();
                DrawWires(wiresOverBlended, paletteAddress);
            }
            finally
            {
                if (frame != null)
                {
                    CloseSceneLight(frame);
                }
            }
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
        shadowLight = -1;
        shadowCaster = null;
        // The lights of the scene the output shows; a scene without them is lit by none.
        if (Scene is not { } scene || !scene.Satellites.TryGet<LightManager>(out var lightManager))
        {
            LightCount = 0;
            return 0;
        }

        var count = Append(lightManager.DirectionalLights, camera, 0, castsShadow: true);
        var directional = count;
        count = Append(lightManager.PointLights, camera, count, castsShadow: false);
        count = Append(lightManager.SpotLights, camera, count, castsShadow: false);
        LightCount = count;
        return directional;
    }

    // The first light of a kind that casts shadows, and wants to, is the one whose shadow is drawn.
    private int Append(IReadOnlyList<Light> lights, Camera camera, int count, bool castsShadow)
    {
        for (int i = 0; i < lights.Count && count < MaxLights; i++)
        {
            var light = lights[i];
            if (light.IsEnabled && light.Owner is { Visible: true })
            {
                if (castsShadow && shadowLight < 0 && light.IsWithShadows)
                {
                    shadowLight = count;
                    shadowCaster = light;
                }

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
        // Anisotropic, so a surface seen at a slant keeps its detail sharp along it without shimmering across it.
        parameters["sampleType"].SetResource(SamplerStates.AnisotropicRepeat);
        parameters["LinearFrame"].SetValue(linearFrame ? 1u : 0u);
        var shadowed = shadowCaster != null;
        var descriptors = ((GraphicsDevice)GraphicsDevice).DescriptorManager;
        var withSky = sky != SkyKind.None && skyMade == sky;
        parameters["Sky"].SetValue(withSky ? (uint)sky : 0u);
        parameters["SkyStrength"].SetValue(skyStrength);
        parameters["SkyExposure"].SetValue(MathF.Pow(2f, exposure));
        if (withSky)
        {
            parameters["WorldFromView"].SetValue(Matrix4x4F.Invert(camera.ViewMatrix));
            parameters["SkyIrradiance"].SetValue(descriptors.TextureIndex(skyIrradiance));
            for (var level = 0; level < SkyReflectionLevels; level++)
            {
                parameters[SkyReflectionNames[level]].SetValue(descriptors.TextureIndex(skyReflections[level]));
            }
        }

        parameters["ShadowMap"].SetValue(shadowed ? descriptors.TextureIndex(shadowMap) : MeshInstance.NoMap);
        parameters["ShadowLight"].SetValue(shadowed ? (uint)shadowLight : MeshInstance.NoMap);
        if (shadowed)
        {
            parameters["ShadowCascade0"].SetValue(cascadeLookups[0]);
            parameters["ShadowCascade1"].SetValue(cascadeLookups[1]);
            parameters["ShadowCascade2"].SetValue(cascadeLookups[2]);
            parameters["ShadowCascade3"].SetValue(cascadeLookups[3]);
            parameters["ShadowSplits"].SetValue(new Vector4F(cascadeEnds[0], cascadeEnds[1], cascadeEnds[2],
                cascadeEnds[3]));
            parameters["ShadowTexels"].SetValue(new Vector4F(cascadeTexels[0], cascadeTexels[1], cascadeTexels[2],
                cascadeTexels[3]));
            parameters["ShadowStep"].SetValue(1f / shadowMap.Width);
        }
    }

    private static readonly string[] SkyReflectionNames =
        ["SkyReflection0", "SkyReflection1", "SkyReflection2", "SkyReflection3", "SkyReflection4", "SkyReflection5"];

    // The sky's pictures for the physical surfaces, made once for a kind of sky, before the frame's pass: the light a matt
    // surface gathers round each normal, and the sky mirrored at each roughness.
    private void MakeSky()
    {
        var device = GraphicsDevice;
        var targets = device.CurrentRenderTarget;
        var depth = device.CurrentDepthStencilBuffer;
        var samples = device.MSAALevel;
        EnsureSkyPictures();
        effect.Parameters["Sky"].SetValue((uint)sky);
        device.SetDepthBuffer(null);
        device.MSAALevel = MSAALevel.None;
        try
        {
            effect.Parameters["SkyStep"].SetValue(new Vector2F(1f / skyIrradiance.Width, 1f / skyIrradiance.Height));
            DrawPicture(effect.ForwardPlusSkyIrradiancePass, skyIrradiance, ColorBlendEquations.Opaque, adding: false);
            for (var level = 0; level < SkyReflectionLevels; level++)
            {
                var picture = skyReflections[level];
                effect.Parameters["SkyStep"].SetValue(new Vector2F(1f / picture.Width, 1f / picture.Height));
                effect.Parameters["SkyRoughness"].SetValue(level / (float)(SkyReflectionLevels - 1));
                DrawPicture(effect.ForwardPlusSkyReflectionPass, picture, ColorBlendEquations.Opaque, adding: false);
            }

            skyMade = sky;
        }
        finally
        {
            device.SetDepthBuffer(depth);
            device.MSAALevel = samples;
            frameTargets[0] = targets;
            device.SetRenderTargets(frameTargets);
        }
    }

    private void EnsureSkyPictures()
    {
        if (skyIrradiance is { IsDisposed: false })
        {
            return;
        }

        ReleaseSkyPictures();
        skyIrradiance = GraphicsDevice.CreateRenderTarget(SkyIrradianceWidth, SkyIrradianceWidth / 2, MSAALevel.None,
            Format.R16G16B16A16_SFLOAT, ImageUsageFlagBits.SampledBit, ImageLayout.ColorAttachmentOptimal, "Sky light");
        skyReflections = new IRenderTarget[SkyReflectionLevels];
        for (var level = 0; level < SkyReflectionLevels; level++)
        {
            var width = SkyReflectionWidth >> level;
            skyReflections[level] = GraphicsDevice.CreateRenderTarget(width, width / 2, MSAALevel.None,
                Format.R16G16B16A16_SFLOAT, ImageUsageFlagBits.SampledBit, ImageLayout.ColorAttachmentOptimal,
                $"Sky mirrored {level}");
        }
    }

    private void ReleaseSkyPictures()
    {
        foreach (var picture in (IRenderTarget[])[skyIrradiance, .. skyReflections])
        {
            if (picture is IDisposable disposable && !picture.IsDisposed)
                picture.GraphicsDevice.AddToDeferDisposeQueue(disposable);
        }

        skyIrradiance = null;
        skyReflections = [];
        skyMade = null;
    }

    /// <summary>The sky over the whole frame, before anything else is drawn in it: its
    /// <see cref="SceneBackgroundProcessor"/> calls it. In a high dynamic range frame it goes through the same tone curve
    /// as the scene. Nothing without a sky.</summary>
    internal void DrawBackground()
    {
        if (!prepared || Window?.Camera == null || sky == SkyKind.None || skyMade != sky)
        {
            return;
        }

        DrawOverTarget(effect.ForwardPlusSkyBackgroundPass, ColorBlendEquations.Opaque);
    }

    // A map of four cascades, each the light's resolution square: made anew when that changes.
    private void EnsureShadowMap(int resolution)
    {
        var side = (uint)(resolution > 0 ? Math.Clamp(resolution, 256, LargestCascadeSide) : DefaultCascadeSide);
        if (shadowMap is { IsDisposed: false } && cascadeSide == side)
        {
            return;
        }

        ReleaseShadowMap();
        cascadeSide = side;
        shadowMap = GraphicsDevice.CreateReadableDepthBuffer(side * 2, side * 2, DepthFormat.Depth32, "Shadow map");
    }

    private void ReleaseShadowMap()
    {
        if (shadowMap is IDisposable map && !shadowMap.IsDisposed)
            shadowMap.GraphicsDevice.AddToDeferDisposeQueue(map);
        shadowMap = null;
    }

    // Each cascade a sphere round its slice of the view, the near ones short and the far ones long, seen square along the
    // light. The sphere stays one size however the camera turns, and its middle moves by whole texels of the world, so
    // the shadow's edges do not crawl as the camera moves.
    private void PlaceCascades(Camera camera)
    {
        var near = Math.Max(camera.ZNear, 1e-3f);
        var far = Math.Max(Math.Min(camera.ZFar, ShadowDistance), near * 2);
        var fromView = Matrix4x4F.Invert(camera.ViewMatrix);
        var fromClip = Matrix4x4F.Invert(camera.ProjectionMatrix);
        Span<Vector3F> nearCorners = stackalloc Vector3F[4];
        Span<Vector3F> farCorners = stackalloc Vector3F[4];
        for (var i = 0; i < 4; i++)
        {
            var x = (i & 1) == 0 ? -1f : 1f;
            var y = (i & 2) == 0 ? -1f : 1f;
            nearCorners[i] = Vector3F.TransformCoordinate(new Vector3F(x, y, 0), fromClip);
            farCorners[i] = Vector3F.TransformCoordinate(new Vector3F(x, y, 1), fromClip);
        }

        var direction = shadowCaster.Direction;
        var up = MathF.Abs(direction.Y) > 0.99f ? Vector3F.UnitZ : Vector3F.UnitY;
        var toLight = Matrix4x4.CreateLookToLH(Vector3F.Zero, direction, up);
        var eye = camera.WorldPosition.ToVector();
        // The camera along the light's axes, in double: where whole texels of the world fall.
        var eyeAcross = eye.X * toLight.M11 + eye.Y * toLight.M21 + eye.Z * toLight.M31;
        var eyeUp = eye.X * toLight.M12 + eye.Y * toLight.M22 + eye.Z * toLight.M32;
        var start = near;
        Span<Vector3F> slice = stackalloc Vector3F[8];
        for (var cascade = 0; cascade < Cascades; cascade++)
        {
            var share = (cascade + 1) / (float)Cascades;
            var end = CascadeSpacing * near * MathF.Pow(far / near, share) + (1 - CascadeSpacing) * (near + (far - near) * share);
            var middle = Vector3F.Zero;
            for (var i = 0; i < 4; i++)
            {
                slice[i] = Vector3F.TransformCoordinate(AtDepth(nearCorners[i], farCorners[i], start), fromView);
                slice[i + 4] = Vector3F.TransformCoordinate(AtDepth(nearCorners[i], farCorners[i], end), fromView);
            }

            foreach (var corner in slice)
            {
                middle += corner;
            }

            middle /= slice.Length;
            var radius = 0f;
            foreach (var corner in slice)
            {
                radius = Math.Max(radius, Vector3F.Distance(corner, middle));
            }

            radius = MathF.Ceiling(radius * 16f) / 16f;
            var texel = 2f * radius / cascadeSide;
            var center = Vector3F.TransformCoordinate(middle, toLight);
            var across = (float)(Math.Floor((center.X + eyeAcross) / texel) * texel - eyeAcross);
            var upward = (float)(Math.Floor((center.Y + eyeUp) / texel) * texel - eyeUp);
            // What stands between the light and the slice casts onto it too: the box reaches back towards the light.
            var square = Matrix4x4.CreateOrthoOffCenterLH(across - radius, across + radius, upward - radius,
                upward + radius, center.Z - radius - ShadowDistance, center.Z + radius);
            cascadeViews[cascade] = toLight * square;
            cascadeReaches[cascade] = new BoundingFrustum<float>(cascadeViews[cascade]);
            cascadeLookups[cascade] = fromView * cascadeViews[cascade] * QuarterOf(cascade);
            cascadeEnds[cascade] = end;
            cascadeTexels[cascade] = texel;
            start = end;
        }
    }

    // The opaque surfaces' depth from the light into each cascade's quarter of the map, beside the frame; the map left to
    // be read. A see-through surface casts none.
    private void DrawShadow()
    {
        var device = GraphicsDevice;
        var frame = device.CurrentRenderTarget;
        var depth = device.CurrentDepthStencilBuffer;
        var samples = device.MSAALevel;
        var biased = device.DepthBiasEnabled;
        var constantBias = device.DepthBiasConstantFactor;
        var slopeBias = device.DepthBiasSlopeFactor;
        device.SuspendRendering();
        try
        {
            KeepDepthForNextPass();
            device.InsertImageMemoryBarrier(device.CurrentCommandBuffer, shadowMap, AccessFlagBits2.ShaderReadBit,
                AccessFlagBits2.DepthStencilAttachmentWriteBit, ImageLayout.Undefined,
                ImageLayout.DepthStencilAttachmentOptimal, PipelineStageFlagBits2.FragmentShaderBit,
                PipelineStageFlagBits2.EarlyFragmentTestsBit | PipelineStageFlagBits2.LateFragmentTestsBit);
            device.SetRenderTargets(noTargets);
            device.SetDepthBuffer(shadowMap);
            device.MSAALevel = MSAALevel.None;
            // Pushed back a little, more where a surface slopes away from the light, so it does not shadow itself.
            device.DepthBiasEnabled = true;
            device.DepthBiasConstantFactor = 1.25f;
            device.DepthBiasSlopeFactor = 1.75f;
            device.BeginRendering(device.CurrentCommandBuffer);
            try
            {
                for (var cascade = 0; cascade < Cascades; cascade++)
                {
                    DrawCascade(cascade);
                }
            }
            finally
            {
                device.SuspendRendering();
                device.InsertImageMemoryBarrier(device.CurrentCommandBuffer, shadowMap,
                    AccessFlagBits2.DepthStencilAttachmentWriteBit, AccessFlagBits2.ShaderReadBit,
                    ImageLayout.DepthStencilAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                    PipelineStageFlagBits2.EarlyFragmentTestsBit | PipelineStageFlagBits2.LateFragmentTestsBit,
                    PipelineStageFlagBits2.FragmentShaderBit);
            }
        }
        finally
        {
            device.DepthBiasEnabled = biased;
            device.DepthBiasConstantFactor = constantBias;
            device.DepthBiasSlopeFactor = slopeBias;
            device.MSAALevel = samples;
            device.SetDepthBuffer(depth);
            frameTargets[0] = frame;
            device.SetRenderTargets(frameTargets);
            device.SetViewports(Window.Viewport);
            device.SetScissors(Window.Scissor);
            effect.ViewProjection.SetValue(ActiveCamera.ViewProjectionMatrix);
            device.ResumeRendering();
        }
    }

    private void DrawCascade(int cascade)
    {
        var device = GraphicsDevice;
        var x = (int)cascadeSide * (cascade & 1);
        var y = (int)cascadeSide * (cascade >> 1);
        device.SetViewports(new Viewport
        {
            X = x, Y = y, Width = cascadeSide, Height = cascadeSide, MinDepth = 0, MaxDepth = 1
        });
        device.SetScissors(new Rect2D
        {
            Offset = new Offset2D { X = x, Y = y }, Extent = new Extent2D { Width = cascadeSide, Height = cascadeSide }
        });
        effect.ViewProjection.SetValue(cascadeViews[cascade]);
        DrawCasters(groupsDrawn);
        DrawCasters(casterGroups);
    }

    private void DrawCasters(List<MeshDrawGroup> casters)
    {
        var device = GraphicsDevice;
        foreach (var group in casters)
        {
            var key = group.Key;
            if (!key.DepthWrite || key.WireFrame)
            {
                continue;
            }

            var skinned = group.Instances[0].PaletteCount > 0;
            effect.Parameters["InstanceBase"].SetValue(group.First);
            (skinned ? effect.ForwardPlusSkinnedShadowDepthPass : effect.ForwardPlusShadowDepthPass).Apply();
            // Both sides cast: a single-sided wall still stops the light.
            key.Geometry.Draw(device, new RenderState(false, CullModeFlagBits.None, true, true, key.Topology),
                (uint)group.Instances.Count);
        }
    }

    // Where along the view's ray through two corners its depth is the given one.
    private static Vector3F AtDepth(Vector3F nearCorner, Vector3F farCorner, float depth)
    {
        return Vector3F.Lerp(nearCorner, farCorner, (depth - nearCorner.Z) / (farCorner.Z - nearCorner.Z));
    }

    // From a cascade's clip space to its quarter of the map: the first two across the top, the last two below.
    private static Matrix4x4F QuarterOf(int cascade)
    {
        var quarter = Matrix4x4F.Identity;
        quarter.M11 = 0.25f;
        quarter.M22 = 0.25f;
        quarter.M41 = 0.25f + 0.5f * (cascade & 1);
        quarter.M42 = 0.25f + 0.5f * (cascade >> 1);
        return quarter;
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
            // Out of view, it may still stand between the light and what is seen.
            if (shadowCaster != null && shown is not (EntityDisplay.Wireframe or EntityDisplay.Collider)
                && entity.GetComponent<Material>() is not { AlphaMode: AlphaMode.Blend } && InShadowReach(entity))
            {
                EnqueueCaster(entity, meshData);
            }

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
            // The box against the camera, taken away in double, as the frustum is.
            return frustum.Contains(new BoundingBox<float>((box.Minimum - cameraPosition.ToVector()).As<float>(),
                (box.Maximum - cameraPosition.ToVector()).As<float>())) != ContainmentType.Disjoint;
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
            GizmoInstance[] copy = [new GizmoInstance(wire.World, new Vector4F(InFrame(WireColor), 1f))];
            basicEffect.Instances.SetValue(GraphicsDevice.UploadFrameData<GizmoInstance>(copy));
            basicEffect.BasicSkinnedFlatInstancedPass.Apply();
        }
        else
        {
            basicEffect.Wvp.SetValue(wire.World * ActiveCamera.ViewProjectionMatrix);
            basicEffect.MeshColor.SetValue(InFrame(WireColor));
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
        var physical = material?.Lighting == LightingModel.Physical;
        var pass = physical
            ? PhysicalPassOf(withWire, skinned, masked: alphaMode == AlphaMode.Mask, seeThrough)
            : PassOf(withWire, skinned, textured, masked: alphaMode == AlphaMode.Mask, seeThrough);
        var geometry = geometryCache.GetOrCreate(meshData.Mesh, meshData.ResolveVertexType());
        var (first, count) = skinned ? PaletteOf(entity.GetComponent<Skin>(), world) : default;
        var textureIndex = texture != null ? IndexOf(texture) : physical ? MeshInstance.NoMap : 0;
        var emissive = IndexOf(LiveTexture(material?.EmissiveTexture));
        var maps = physical
            ? new MaterialMaps(IndexOf(LiveTexture(material.MetallicRoughnessTexture)),
                IndexOf(LiveTexture(material.NormalTexture)), IndexOf(LiveTexture(material.OcclusionTexture)), emissive)
            : MaterialMaps.None with { Emissive = emissive };
        var surface = material == null
            ? default
            : new Vector4F(material.MetallicFactor, material.RoughnessFactor, material.NormalScale, material.OcclusionStrength);
        var emission = material == null ? default : material.Emission with { W = 0 };
        var instance = new MeshInstance(world, albedo, first, count, textureIndex, alphaCutoff, emission, surface, maps);

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

    // Whether an entity's box in the scene's index lies in any cascade's box, which reaches back towards the light.
    private bool InShadowReach(Entity entity)
    {
        if (sceneIndex == null || !sceneIndex.TryGetBox(entity, out var box))
        {
            return false;
        }

        var near = new BoundingBox<float>((box.Minimum - cameraPosition.ToVector()).As<float>(),
            (box.Maximum - cameraPosition.ToVector()).As<float>());
        foreach (var reach in cascadeReaches)
        {
            if (reach.Contains(near) != ContainmentType.Disjoint)
            {
                return true;
            }
        }

        return false;
    }

    // A mesh out of view that only casts: drawn into the shadow map and nowhere else.
    private void EnqueueCaster(Entity entity, MeshData meshData)
    {
        var transformation = entity.Transform.GetMetadata(ActiveCamera);
        if (!transformation.Enabled)
        {
            return;
        }

        var world = transformation.WorldMatrixF;
        var skinned = meshData.RenderMode == MeshRenderMode.Skinned;
        var (first, count) = skinned ? PaletteOf(entity.GetComponent<Skin>(), world) : default;
        var instance = new MeshInstance(world, default, first, count, MeshInstance.NoMap, 0f, default, default,
            MaterialMaps.None);
        var geometry = geometryCache.GetOrCreate(meshData.Mesh, meshData.ResolveVertexType());
        var pass = skinned ? effect.ForwardPlusSkinnedShadowDepthPass : effect.ForwardPlusShadowDepthPass;
        var key = new MeshDrawKey(geometry, pass, CullModeFlagBits.None, true, true, false, meshData.TopologyOverride);
        if (!groups.TryGetValue(key, out var group))
        {
            group = spareGroups.Count > 0 ? spareGroups.Pop() : new MeshDrawGroup();
            group.Key = key;
            group.Effect = effect;
            groups.Add(key, group);
            casterGroups.Add(group);
        }

        group.Instances.Add(instance);
    }

    private uint IndexOf(Texture texture) =>
        texture == null ? MeshInstance.NoMap : ((GraphicsDevice)GraphicsDevice).DescriptorManager.TextureIndex(texture);

    private static Texture LiveTexture(Texture texture) => texture is { IsDisposed: false } ? texture : null;

    // A physical pass reads a texture when it has one, so one pass serves a surface with or without.
    private IEffectPass PhysicalPassOf(bool withWire, bool skinned, bool masked, bool seeThrough)
    {
        if (seeThrough)
            return skinned ? effect.ForwardPlusSkinnedWeightedPhysicalPass : effect.ForwardPlusWeightedPhysicalPass;
        if (withWire)
        {
            if (masked)
                return skinned ? wireEffect.ForwardPlusWireSkinnedMaskedPhysicalPass : wireEffect.ForwardPlusWireMaskedPhysicalPass;
            return skinned ? wireEffect.ForwardPlusWireSkinnedPhysicalPass : wireEffect.ForwardPlusWirePhysicalPass;
        }

        if (masked)
            return skinned ? effect.ForwardPlusSkinnedMaskedPhysicalPass : effect.ForwardPlusMaskedPhysicalPass;
        return skinned ? effect.ForwardPlusSkinnedPhysicalPass : effect.ForwardPlusPhysicalPass;
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
        count = PlaceGroups(casterGroups, count);
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
        CopyInstances(casterGroups);
        var instanceAddress = GraphicsDevice.UploadFrameData<MeshInstance>(instances.AsSpan(0, count));
        foreach (var target in (ReadOnlySpan<Effect>)[effect, wireEffect])
        {
            target?.Parameters["Instances"].SetValue(instanceAddress);
            target?.Parameters["Palettes"].SetValue(paletteAddress);
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
        var descriptors = ((GraphicsDevice)GraphicsDevice).DescriptorManager;
        effect.Parameters["AccumulatedTexture"].SetValue(descriptors.TextureIndex(weightedSums.ResolveTexture));
        effect.Parameters["CoverageTexture"].SetValue(descriptors.TextureIndex(weightedCoverage.ResolveTexture));
        DrawOverTarget(effect.ForwardPlusWeightedCompositePass, ColorBlendEquations.AlphaBlend);
    }

    // One triangle over the whole target by the pass, blended so, neither tested nor writing depth.
    private void DrawOverTarget(IEffectPass pass, ColorBlendEquationEXT blendEquation)
    {
        var device = GraphicsDevice;
        var blend = device.ColorBlendEquation;
        var vertexTypes = device.VertexTypes;
        var topology = device.PrimitiveTopology;
        var polygonMode = device.PolygonMode;
        var cullMode = device.CullMode;
        var depthTest = device.DepthTestEnabled;
        var depthWrite = device.DepthWriteEnable;
        device.ColorBlendEquation = blendEquation;
        device.VertexType = null;
        device.PrimitiveTopology = PrimitiveTopology.TriangleList;
        device.PolygonMode = PolygonMode.Fill;
        device.CullMode = CullModeFlagBits.None;
        device.DepthTestEnabled = false;
        device.DepthWriteEnable = false;
        try
        {
            pass.Apply();
            device.Draw(3, 1);
        }
        finally
        {
            device.ColorBlendEquation = blend;
            device.SetVertexTypes(vertexTypes);
            device.PrimitiveTopology = topology;
            device.PolygonMode = polygonMode;
            device.CullMode = cullMode;
            device.DepthTestEnabled = depthTest;
            device.DepthWriteEnable = depthWrite;
        }
    }

    // Leaves the frame for the linear one beside it, the frame's size and samples and tested against its depth, cleared to
    // nothing; gives the frame to come back to.
    private IRenderTarget OpenSceneLight()
    {
        var device = GraphicsDevice;
        var frame = device.CurrentRenderTarget;
        EnsureSceneLight(frame, device.MSAALevel);
        frameChannels = device.ColorComponentFlags;
        frameClearColor = device.ClearColor;
        device.SuspendRendering();
        try
        {
            KeepDepthForNextPass();
            PrepareToDraw(sceneLight);
            sceneLightTargets[0] = sceneLight;
            device.SetRenderTargets(sceneLightTargets);
            device.ClearColor = Colors.Transparent;
            // Its alpha is how much the scene covers, which the tone curve lays it over the frame by.
            device.ColorComponentFlags = AllChannels;
            device.BeginRendering(device.CurrentCommandBuffer, keepDepth: true);
        }
        catch
        {
            ReturnToFrame(frame);
            throw;
        }

        return frame;
    }

    // Closes the linear frame, makes its glow, and lays it over the frame through the tone curve.
    private void CloseSceneLight(IRenderTarget frame)
    {
        var device = GraphicsDevice;
        device.SuspendRendering();
        PrepareToRead(sceneLight);
        var samples = !ReferenceEquals(sceneLight.ResolveTexture, sceneLight);
        if (samples)
        {
            // The tone curve reads each sample of it; the glow, the resolved copy.
            device.InsertImageMemoryBarrier(device.CurrentCommandBuffer, sceneLight, AccessFlagBits2.ColorAttachmentWriteBit,
                AccessFlagBits2.ShaderReadBit, ImageLayout.ColorAttachmentOptimal, ImageLayout.ShaderReadOnlyOptimal,
                PipelineStageFlagBits2.ColorAttachmentOutputBit, PipelineStageFlagBits2.FragmentShaderBit);
        }

        KeepDepthForNextPass();
        var glowTexture = MeshInstance.NoMap;
        try
        {
            glowTexture = glow > 0 && glowCopies.Length > 0 ? DrawGlow() : MeshInstance.NoMap;
        }
        finally
        {
            ReturnToFrame(frame);
        }

        var descriptors = ((GraphicsDevice)device).DescriptorManager;
        var parameters = effect.Parameters;
        parameters["SceneLightTexture"].SetValue(descriptors.TextureIndex(samples ? sceneLight : sceneLight.ResolveTexture));
        parameters["SceneLightSamples"].SetValue(samples ? (uint)sceneLight.MSAALevel : 1u);
        parameters["GlowTexture"].SetValue(glowTexture);
        parameters["ToneMapping"].SetValue(new Vector4F(MathF.Pow(2f, exposure), Math.Clamp(glow, 0f, 1f),
            Math.Max(1, glowCopies.Length), 0f));
        parameters["FrameStep"].SetValue(new Vector2F(1f / sceneLight.Width, 1f / sceneLight.Height));
        DrawOverTarget(effect.ForwardPlusToneMapPass, ColorBlendEquations.Premultiplied);
    }

    private void ReturnToFrame(IRenderTarget frame)
    {
        var device = GraphicsDevice;
        device.ColorComponentFlags = frameChannels;
        device.ClearColor = frameClearColor;
        frameTargets[0] = frame;
        device.SetRenderTargets(frameTargets);
        device.ResumeRendering();
    }

    // The glow: the linear frame copied smaller and smaller, then each copy blurred and added to the one twice its size.
    // Gives the biggest copy's texture.
    private uint DrawGlow()
    {
        var device = GraphicsDevice;
        var descriptors = ((GraphicsDevice)device).DescriptorManager;
        var depth = device.CurrentDepthStencilBuffer;
        var samples = device.MSAALevel;
        var clearColor = device.ClearColor;
        // The copies have neither depth nor samples.
        device.SetDepthBuffer(null);
        device.MSAALevel = MSAALevel.None;
        device.ClearColor = Colors.Transparent;
        try
        {
            ITexture source = sceneLight.ResolveTexture;
            foreach (var copy in glowCopies)
            {
                DrawGlowStep(effect.ForwardPlusGlowDownPass, source, copy, ColorBlendEquations.Opaque, adding: false);
                source = copy;
            }

            for (var i = glowCopies.Length - 2; i >= 0; i--)
            {
                DrawGlowStep(effect.ForwardPlusGlowUpPass, glowCopies[i + 1], glowCopies[i], ColorBlendEquations.Sum,
                    adding: true);
            }

            return descriptors.TextureIndex(glowCopies[0].ResolveTexture);
        }
        finally
        {
            device.SetDepthBuffer(depth);
            device.MSAALevel = samples;
            device.ClearColor = clearColor;
            device.SetViewports(Window.Viewport);
            device.SetScissors(Window.Scissor);
        }
    }

    // One step of the glow: the source read into the target, which it replaces or is added to.
    private void DrawGlowStep(IEffectPass pass, ITexture source, IRenderTarget target, ColorBlendEquationEXT blend,
        bool adding)
    {
        var descriptors = ((GraphicsDevice)GraphicsDevice).DescriptorManager;
        effect.Parameters["GlowSource"].SetValue(descriptors.TextureIndex(source));
        effect.Parameters["GlowStep"].SetValue(new Vector4F(1f / target.Width, 1f / target.Height, 1f / source.Width,
            1f / source.Height));
        DrawPicture(pass, target, blend, adding);
    }

    // A picture with neither depth nor samples drawn whole by one pass, which replaces or is added to what it holds; left
    // to be read.
    private void DrawPicture(IEffectPass pass, IRenderTarget target, ColorBlendEquationEXT blend, bool adding)
    {
        var device = GraphicsDevice;
        if (adding)
        {
            PrepareToAddTo(target);
        }
        else
        {
            PrepareToDraw(target);
        }

        pictureTargets[0] = target;
        device.SetRenderTargets(pictureTargets);
        device.SetViewports(new Viewport { X = 0, Y = 0, Width = target.Width, Height = target.Height, MinDepth = 0, MaxDepth = 1 });
        device.SetScissors(new Rect2D
        {
            Offset = new Offset2D(), Extent = new Extent2D { Width = target.Width, Height = target.Height }
        });
        device.BeginRendering(device.CurrentCommandBuffer, continueRendering: adding);
        try
        {
            DrawOverTarget(pass, blend);
        }
        finally
        {
            device.SuspendRendering();
            PrepareToRead(target);
        }
    }

    // A copy read by the step before, written again over what it holds.
    private void PrepareToAddTo(IRenderTarget layer)
    {
        GraphicsDevice.InsertImageMemoryBarrier(GraphicsDevice.CurrentCommandBuffer, layer, AccessFlagBits2.ShaderReadBit,
            AccessFlagBits2.ColorAttachmentReadBit | AccessFlagBits2.ColorAttachmentWriteBit,
            ImageLayout.ShaderReadOnlyOptimal, ImageLayout.ColorAttachmentOptimal, PipelineStageFlagBits2.FragmentShaderBit,
            PipelineStageFlagBits2.ColorAttachmentOutputBit);
    }

    // The linear frame the size and samples of the frame, and the glow's copies, each half the one before, from half the
    // frame's size: made anew when the frame changes, the old ones let go once the frames in flight are done with them.
    private void EnsureSceneLight(IRenderTarget frame, MSAALevel samples)
    {
        if (sceneLight is { IsDisposed: false } && sceneLight.Width == frame.Width
            && sceneLight.Height == frame.Height && sceneLight.MSAALevel == samples)
        {
            return;
        }

        ReleaseSceneLight();
        sceneLight = GraphicsDevice.CreateRenderTarget(frame.Width, frame.Height, samples,
            Format.R16G16B16A16_SFLOAT, ImageUsageFlagBits.SampledBit, ImageLayout.ColorAttachmentOptimal, "Scene light");
        var copies = new List<IRenderTarget>();
        var width = frame.Width / 2;
        var height = frame.Height / 2;
        while (copies.Count < MostGlowCopies && Math.Min(width, height) >= SmallestGlow)
        {
            copies.Add(GraphicsDevice.CreateRenderTarget(width, height, MSAALevel.None, Format.R16G16B16A16_SFLOAT,
                ImageUsageFlagBits.SampledBit, ImageLayout.ColorAttachmentOptimal, $"Glow {copies.Count + 1}"));
            width /= 2;
            height /= 2;
        }

        glowCopies = copies.ToArray();
    }

    private void ReleaseSceneLight()
    {
        if (sceneLight is IDisposable light && !sceneLight.IsDisposed)
            sceneLight.GraphicsDevice.AddToDeferDisposeQueue(light);
        foreach (var copy in glowCopies)
        {
            if (copy is IDisposable disposable && !copy.IsDisposed)
                copy.GraphicsDevice.AddToDeferDisposeQueue(disposable);
        }

        sceneLight = null;
        glowCopies = [];
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
        Recycle(casterGroups);
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
        basicEffect.MeshColor.SetValue(InFrame(ColliderColor));
        basicEffect.BasicColoredPass.Apply();
        if (collider.GetVisualRepresentation() is { } outline)
        {
            geometryCache.GetOrCreate(outline, typeof(MeshVertex)).Draw(GraphicsDevice, RenderState.Default);
        }
    }

    // A color given as the screen shows it, as the frame takes it: decoded from sRGB for a linear frame.
    private Vector3F InFrame(Vector3F color)
    {
        return linearFrame ? new Vector3F(Linear(color.X), Linear(color.Y), Linear(color.Z)) : color;
    }

    private static float Linear(float srgb)
    {
        return srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow((srgb + 0.055f) / 1.055f, 2.4f);
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
        ReleaseSceneLight();
        ReleaseShadowMap();
        ReleaseSkyPictures();
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
