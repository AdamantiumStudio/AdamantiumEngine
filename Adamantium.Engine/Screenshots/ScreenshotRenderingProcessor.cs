using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Mathematics;
using Adamantium.Multiverse;

namespace Adamantium.Engine.Screenshots;

/// <summary>Takes the screenshot of one entity at a time, handed to it by its <see cref="ScreenshotRenderingService"/>:
/// aims a camera of its own so every corner of the entity's bounds is in view - from the front, above and to the right -
/// works out where the entity and its children stand for that camera, as no world does it for an entity in none, and
/// draws it alone, in the light given, over a transparent ground; nothing of the world beside it, none of its lights.
/// </summary>
public sealed class ScreenshotRenderingProcessor : ForwardPlusRenderingProcessor
{
    private static readonly Vector3<double> ViewDirection = Vector3.Normalize(new Vector3<double>(1, -0.8, -1.2));

    private const double Fill = 0.92;

    private readonly Entity[] _drawn = new Entity[1];
    private readonly Vector3<double>[] _corners = new Vector3<double>[8];
    private Entity _target;
    private Camera _camera;
    private ScreenshotLighting _lighting = ScreenshotLighting.Studio();
    private ScreenshotLighting _frameLighting;

    public ScreenshotRenderingProcessor()
    {
        ClearColor = Colors.Transparent;
    }

    /// <summary>The light screenshots are taken in, taken once a frame; set from any thread.</summary>
    public ScreenshotLighting Lighting
    {
        get => Volatile.Read(ref _lighting);
        set => Volatile.Write(ref _lighting, value ?? ScreenshotLighting.Studio());
    }

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
        _target = entity;
        _drawn[0] = entity;
        return true;
    }

    /// <summary>Lets go of the entity taken and its camera.</summary>
    public void Release()
    {
        _target = null;
        _camera = null;
        _drawn[0] = null;
        ActiveCamera = null;
    }

    protected override Camera ViewCamera => _camera;

    protected override IReadOnlyList<Entity> DrawnEntities => _target == null ? [] : _drawn;

    protected override IReadOnlyList<Light> DirectionalLights => FrameLighting.DirectionalLights;

    protected override IReadOnlyList<Light> PointLights => FrameLighting.PointLights;

    protected override IReadOnlyList<Light> SpotLights => FrameLighting.SpotLights;

    private ScreenshotLighting FrameLighting => _frameLighting ?? Lighting;

    public override void PreRender()
    {
        _frameLighting = Lighting;
        Ambient = _frameLighting.Ambient;
        base.PreRender();
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
}
