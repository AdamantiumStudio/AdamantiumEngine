using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Rendering;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.FX;
using Adamantium.Graphics.Core.Vertices;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Serilog;

namespace Adamantium.Engine.Screenshots;

/// <summary>Takes one screenshot at a time, handed to it by its <see cref="ScreenshotRenderingService"/>, with a
/// rendering of its own - <see cref="ScreenshotEffect"/>: the colors or textures of the materials in a few directional
/// lights and an even light, nothing of the scene's passes. Of an entity: it aims a camera of its own so every corner of
/// the entity's bounds is in view - from the front, above and to the right. Of a whole scene: a camera of its own stands
/// where the camera given stands and looks as it looks. Either way it works out where what it draws stands for that
/// camera - no world does it for its camera - and draws those meshes over a transparent ground. The GPU geometry it made
/// goes once the screenshot is done.</summary>
public sealed class ScreenshotRenderingProcessor : RenderingProcessor
{
    private static readonly Vector3<double> ViewDirection = Vector3.Normalize(new Vector3<double>(1, -0.8, -1.2));

    private const double Fill = 0.92;

    private static readonly Vector3F UntexturedColor = new(0.75f, 0.75f, 0.75f);

    private readonly Vector3<double>[] _corners = new Vector3<double>[8];
    private readonly Stack<Entity> _walk = new();
    private ScreenshotLight[] _lights = [];
    private ScreenshotEffect _effect;
    private MeshGeometryCache _geometry;
    private readonly List<Entity> _targets = [];
    private Camera _camera;
    private ScreenshotLighting _lighting = ScreenshotLighting.Studio();
    private ScreenshotLighting _frameLighting;

    /// <summary>The light screenshots are taken in, taken once a frame; set from any thread.</summary>
    public ScreenshotLighting Lighting
    {
        get => Volatile.Read(ref _lighting);
        set => Volatile.Write(ref _lighting, value ?? ScreenshotLighting.Studio());
    }

    /// <summary>Whether the last frame drew what was taken through, every mesh of it.</summary>
    public bool DrewFrame { get; private set; }

    /// <summary>Makes <paramref name="entity"/> what the next frame draws, the camera aimed at it; false, and nothing
    /// to draw, for an entity without bounds.</summary>
    public bool Take(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.GetComponent<Collider>() is not { } bounds || entity.GetDiameter() <= 0)
        {
            return false;
        }

        _camera = Aim(entity, bounds);
        Place(entity, _camera);
        _targets.Add(entity);
        return true;
    }

    /// <summary>Makes all of <paramref name="scene"/> what the next frame draws, seen as <paramref name="viewpoint"/>
    /// sees it - from where it stands, looking where it looks, with its field of view and projection - in the shape of
    /// this processor's output. On the world's thread.</summary>
    public void Take(Scene scene, Camera viewpoint)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(viewpoint);
        _camera = CopyOf(viewpoint);
        foreach (var root in scene.RootEntities)
        {
            Place(root, _camera);
            _targets.Add(root);
        }
    }

    /// <summary>Lets go of what was taken, its camera and the GPU geometry made for it.</summary>
    public void Release()
    {
        _targets.Clear();
        _camera = null;
        ActiveCamera = null;
        _geometry?.Clear();
    }

    protected override void CreateDeviceResources()
    {
        ReleaseDeviceResources();
        _effect = new ScreenshotEffect(GraphicsDevice);
        _geometry = new MeshGeometryCache(GraphicsDevice);
        base.CreateDeviceResources();
    }

    protected override void OnDetached()
    {
        ReleaseDeviceResources();
        base.OnDetached();
    }

    public override void PreRender()
    {
        GraphicsDevice.ClearColor = Colors.Transparent;
        _frameLighting = Lighting;
    }

    public override void Draw(AppTime appTime)
    {
        DrewFrame = false;
        if (_camera == null || _effect == null)
        {
            return;
        }

        ActiveCamera = _camera;
        try
        {
            SetLights();
            foreach (var target in _targets)
            {
                DrawBranch(target);
            }

            DrewFrame = true;
        }
        catch (Exception exception)
        {
            Log.Logger.Error(exception, "The screenshot of {Count} entities failed", _targets.Count);
        }
        finally
        {
            _walk.Clear();
        }
    }

    private void SetLights()
    {
        var lights = _frameLighting.DirectionalLights;
        if (_lights.Length < lights.Count)
        {
            _lights = new ScreenshotLight[lights.Count];
        }

        var count = 0;
        foreach (var light in lights)
        {
            if (light.IsEnabled)
            {
                _lights[count++] = new ScreenshotLight(new Vector4F(Vector3F.Normalize(light.Direction), 0),
                    new Vector4F(light.Color * light.Intensity, 0));
            }
        }

        _effect.Lights.SetValue(GraphicsDevice.UploadFrameData<ScreenshotLight>(_lights.AsSpan(0, count)));
        _effect.LightCount.SetValue((uint)count);
        _effect.Ambient.SetValue(_frameLighting.Ambient);
        _effect.SampleType.SetResource(SamplerStates.LinearRepeat);
    }

    private void DrawBranch(Entity root)
    {
        _walk.Push(root);
        while (_walk.Count > 0)
        {
            var entity = _walk.Pop();
            if (!entity.Visible)
            {
                continue;
            }

            if (entity.GetComponent<MeshData>() is { Mesh: { HasPoints: true } mesh, IsEnabled: true } meshData)
            {
                DrawMesh(entity, meshData, mesh);
            }

            foreach (var child in entity.Dependencies.AsSpan())
            {
                if (child != null)
                {
                    _walk.Push(child);
                }
            }
        }
    }

    private void DrawMesh(Entity entity, MeshData meshData, Graphics.Core.Models.Mesh mesh)
    {
        var world = entity.Transform.GetMetadata(_camera).WorldMatrixF;
        var material = entity.GetComponent<Material>();
        var texture = material?.Texture is { IsDisposed: false } live ? live : null;
        _effect.Wvp.SetValue(world * _camera.ViewProjectionMatrix);
        _effect.World.SetValue(world);
        _effect.Albedo.SetValue(AlbedoOf(material));
        _effect.TextureIndex.SetValue(texture != null
            ? ((GraphicsDevice)GraphicsDevice).DescriptorManager.TextureIndex(texture)
            : 0u);
        (texture != null ? _effect.ScreenshotTexturedPass : _effect.ScreenshotColoredPass).Apply();
        var state = new RenderState(false, meshData.CullMode, meshData.DepthTestEnabled, meshData.DepthWriteEnabled,
            meshData.TopologyOverride);
        _geometry.GetOrCreate(mesh, typeof(MeshVertex)).Draw(GraphicsDevice, state);
    }

    private static Vector3F AlbedoOf(Material material)
    {
        if (material == null)
        {
            return UntexturedColor;
        }

        var diffuse = material.DiffuseColor;
        return diffuse.X + diffuse.Y + diffuse.Z > 0 ? new Vector3F(diffuse.X, diffuse.Y, diffuse.Z) : UntexturedColor;
    }

    private Camera Aim(Entity entity, Collider bounds)
    {
        bounds.Bounds.GetCorners(_corners);
        var world = entity.Transform.GetWorldMatrixF();
        for (var i = 0; i < _corners.Length; i++)
        {
            _corners[i] = Vector3F.TransformCoordinate(_corners[i].As<float>(), world).As<double>();
        }

        var center = entity.GetCenterAbsolute();
        var forward = -ViewDirection;
        var right = Vector3.Normalize(Vector3<double>.Cross(-Vector3<double>.Up, forward));
        var up = Vector3<double>.Cross(forward, right);
        var cameraEntity = new CameraTemplate().BuildEntity(null, "Screenshot camera", Vector3<double>.Zero, forward,
            -Vector3<double>.Up, Window.Width, Window.Height, 0.1f, 1000f);
        var camera = cameraEntity.GetComponent<Camera>();
        var reach = Math.Tan(double.DegreesToRadians(camera.Fov) / 2) * Fill;
        double distance = 0;
        double radius = 0;
        foreach (var corner in _corners)
        {
            var offset = corner - center;
            var depth = Vector3<double>.Dot(offset, forward);
            var across = Math.Max(Math.Abs(Vector3<double>.Dot(offset, right)), Math.Abs(Vector3<double>.Dot(offset, up)));
            distance = Math.Max(distance, across / reach - depth);
            radius = Math.Max(radius, offset.Length());
        }

        cameraEntity.Transform.Position = center + ViewDirection * distance;
        camera.ZNear = (float)Math.Max(distance - radius * 1.5, distance / 1000);
        camera.ZFar = (float)(distance + radius * 1.5);
        camera.SetFreeLookAt(center);
        camera.Update(default);
        return camera;
    }

    // A camera of the processor's own, where the one given stands and looking as it looks, in the output's shape.
    private Camera CopyOf(Camera viewpoint)
    {
        var cameraEntity = new CameraTemplate().BuildEntity(null, "Screenshot camera", viewpoint.WorldPosition,
            Vector3<double>.ForwardLH, -Vector3<double>.Up, Window.Width, Window.Height, viewpoint.ZNear, viewpoint.ZFar);
        var camera = cameraEntity.GetComponent<Camera>();
        camera.Rotation = viewpoint.Rotation;
        camera.Fov = viewpoint.Fov;
        camera.OrthoScale = viewpoint.OrthoScale;
        camera.ProjectionType = viewpoint.ProjectionType;
        camera.Update(default);
        return camera;
    }

    private static void Place(Entity root, Camera camera)
    {
        var cameraWorld = camera.WorldPosition;
        var rootParent = root.Owner?.Transform?.GetWorldMatrixF() ?? Matrix4x4F.Identity;
        root.TraverseInDepth(current =>
        {
            var parentWorld = current == root || current.Owner?.Transform == null
                ? rootParent
                : current.Owner.Transform.GetMetadata(camera).AbsoluteWorld;
            current.Transform.CalculateFinalTransform(camera, parentWorld, cameraWorld);
            foreach (var collider in current.GetComponents<Collider>())
            {
                collider.UpdateForCamera(camera);
            }
        });
    }

    private void ReleaseDeviceResources()
    {
        _geometry?.Dispose();
        _effect?.Dispose();
        _geometry = null;
        _effect = null;
    }
}
