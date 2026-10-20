using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Templates.CameraTemplates;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Engine.Templates.Markers;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Icons for what a scene has but does not draw - lights, cameras, entities with a <see cref="Marker"/> - facing every
/// camera at a steady size on screen. A directional light's icon also has rays running where it shines, a camera's the
/// shape of its view - a pyramid or, flat, a box - running where it looks, with a mark on top. A click on an icon selects
/// its entity. A camera has no icon in its own view.
/// </summary>
public class EntityIcons : EditorProcessor
{
    /// <summary>The size icons have unless set otherwise, in points.</summary>
    public const float DefaultPixels = 36;

    private readonly Entity root = new(null, nameof(EntityIcons));
    private readonly Entity pointIcon = new PointLightIconTemplate().BuildEntity(null, "Point light");
    private readonly Entity spotIcon = new SpotLightIconTemplate().BuildEntity(null, "Spot light");
    private readonly Entity directionalIcon = new DirectionalLightIconTemplate().BuildEntity(null, "Directional light");
    private readonly Entity directionalRays = new DirectionalLightVisualTemplate().BuildEntity(null, "Rays");
    private readonly Entity cameraIcon = new CameraIconTemplate().BuildEntity(null, "Camera");
    private readonly Entity perspectiveRays = new CameraFrustumTemplate().BuildEntity(null, "View", flat: false);
    private readonly Entity flatRays = new CameraFrustumTemplate().BuildEntity(null, "View", flat: true);
    private readonly Entity emptyIcon = new EmptyIconTemplate().BuildEntity(null, "Entity");
    private readonly Entity groupIcon = new GroupIconTemplate().BuildEntity(null, "Group");
    private readonly Dictionary<Entity, Entity> iconOf = new();
    private readonly Dictionary<Entity, Entity> targetOf = new();
    private readonly Dictionary<Entity, Entity> raysOf = new();
    private readonly HashSet<Entity> seen = [];
    private readonly List<Entity> gone = [];

    /// <summary>How big an icon looks, in points: pixels at 100% scale.</summary>
    public float Pixels { get; set; } = DefaultPixels;

    public bool ShowsLights { get; set; } = true;

    public bool ShowsCameras { get; set; } = true;

    public override void Update(AppTime appTime)
    {
        seen.Clear();
        foreach (var root in Tools.EntityWorld.RootEntities.AsSpan())
        {
            root?.TraverseInDepth(Collect);
        }

        DropGone();

        var cameras = Tools.Observatory.CurrentCameras;
        foreach (var (target, icon) in iconOf)
        {
            for (int i = 0; i < cameras.Count; i++)
            {
                Place(target, icon, cameras[i]);
            }
        }
    }

    /// <summary>Puts an icon at <paramref name="at"/> in the world, facing the camera, <paramref name="pixels"/> points
    /// big on its screen.</summary>
    public static void PlaceIcon(Entity icon, Vector3 at, float pixels, CameraBase camera)
    {
        var size = pixels * ScreenSpace.UnitsPerPoint(camera, at);
        var metadata = icon.Transform.GetMetadata(camera);
        metadata.WorldMatrixF = Matrix4x4F.Scaling(size, -size, size)
                                * Matrix4x4F.RotationQuaternion(ScreenSpace.Facing(camera))
                                * Matrix4x4F.Translation(ScreenSpace.InRender(at, camera));
        metadata.Enabled = true;
    }

    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        overlay.DrawFlatInScene(root);
    }

    /// <summary>The icon that stands for <paramref name="target"/> in the scene, or null when it shows none.</summary>
    public Entity IconOf(Entity target) => target != null && iconOf.TryGetValue(target, out var icon) ? icon : null;

    public override PickHit PickEntity(in PickRay ray)
    {
        var hit = root.Pick(ray, PickMode.MeshColliders);
        if (!hit.IsHit || !targetOf.TryGetValue(hit.Entity, out var target))
        {
            return default;
        }

        return new PickHit(target, hit.Point, hit.Depth);
    }

    public override void PickEntities(CameraBase camera, in ScreenFrame frame, List<Entity> into)
    {
        var half = Pixels * camera.PixelsPerPoint / 2;
        foreach (var target in iconOf.Keys)
        {
            if (target == camera.Owner)
            {
                continue;
            }

            var at = target.GetComponent<Camera>()?.WorldPosition ?? target.Transform.WorldPosition;
            var inRender = ScreenSpace.InRender(at, camera);
            if (Vector3F.Dot(inRender, (Vector3F)camera.Forward) <= camera.ZNear && camera.ProjectionMatrix.M44 < 0.5f)
            {
                continue;
            }

            var center = ScreenSpace.ToPixel(inRender, camera);
            if (frame.Takes(center - new Vector2F(half), center + new Vector2F(half)))
            {
                into.Add(target);
            }
        }
    }

    private void Collect(Entity entity)
    {
        var look = LookOf(entity);
        if (look == null)
        {
            return;
        }

        seen.Add(entity);
        if (!iconOf.TryGetValue(entity, out var icon))
        {
            icon = Instance(root, look);
            icon.AddComponent(new BoxCollider());
            iconOf[entity] = icon;
            targetOf[icon] = entity;
            if (look == directionalIcon)
            {
                raysOf[entity] = Instance(icon, directionalRays);
            }
            else if (look == cameraIcon)
            {
                raysOf[entity] = Instance(icon, perspectiveRays);
            }
        }

        icon.IsSelected = entity.IsSelected;
        if (raysOf.TryGetValue(entity, out var view) && look == cameraIcon)
        {
            view.IsSelected = entity.IsSelected;
        }

        if (entity.GetComponent<Light>() is { } light)
        {
            icon.GetComponent<Material>().MeshColor = light.Color;
            if (raysOf.TryGetValue(entity, out var rays))
            {
                rays.IsSelected = entity.IsSelected;
                rays.GetComponent<Material>().MeshColor = light.Color;
            }
        }
    }

    private Entity LookOf(Entity entity)
    {
        if (ShowsLights && entity.GetComponent<Light>() is { } light)
        {
            return light.Type switch
            {
                LightType.Point => pointIcon,
                LightType.Spot => spotIcon,
                _ => directionalIcon
            };
        }

        if (ShowsCameras && entity.GetComponent<Camera>() != null)
        {
            return cameraIcon;
        }

        if (entity.GetComponent<Marker>() is { IsEnabled: true })
        {
            return entity.IconKey == EntityIconKeys.Group ? groupIcon : emptyIcon;
        }

        return null;
    }

    private static Entity Instance(Entity owner, Entity look)
    {
        var instance = new Entity(owner, look.Name);
        instance.AddComponent(new MeshData { Mesh = look.GetComponent<MeshData>().Mesh });
        var material = look.GetComponent<Material>();
        instance.AddComponent(new Material
        {
            MeshColor = material.MeshColor,
            HighlightColor = material.HighlightColor,
            Transparency = material.Transparency
        });
        return instance;
    }

    private void DropGone()
    {
        gone.Clear();
        foreach (var target in iconOf.Keys)
        {
            if (!seen.Contains(target))
            {
                gone.Add(target);
            }
        }

        for (int i = 0; i < gone.Count; i++)
        {
            var icon = iconOf[gone[i]];
            iconOf.Remove(gone[i]);
            targetOf.Remove(icon);
            raysOf.Remove(gone[i]);
            Tools.EntityWorld.EntityManager.RemoveOwner(icon);
        }
    }

    private void Place(Entity target, Entity icon, Camera camera)
    {
        raysOf.TryGetValue(target, out var rays);
        if (target == camera.Owner)
        {
            icon.Transform.GetMetadata(camera).Enabled = false;
            if (rays != null)
            {
                rays.Transform.GetMetadata(camera).Enabled = false;
            }

            return;
        }

        var at = target.GetComponent<Camera>()?.WorldPosition ?? target.Transform.WorldPosition;
        PlaceIcon(icon, at, Pixels, camera);

        if (rays != null)
        {
            var size = Pixels * ScreenSpace.UnitsPerPoint(camera, at);
            var raysMetadata = rays.Transform.GetMetadata(camera);
            var stretchAndTurn = target.GetComponent<Camera>() is { } seen
                ? ViewOf(seen, rays, size)
                : Matrix4x4F.Scaling(size) * TurnOf(target);
            raysMetadata.WorldMatrixF = stretchAndTurn * Matrix4x4F.Translation(ScreenSpace.InRender(at, camera));
            raysMetadata.Enabled = true;
        }
    }

    private Matrix4x4F ViewOf(Camera seen, Entity rays, float size)
    {
        var projection = seen.ProjectionMatrix;
        var flat = projection.M44 > 0.5f;
        var shape = (flat ? flatRays : perspectiveRays).GetComponent<MeshData>().Mesh;
        var drawn = rays.GetComponent<MeshData>();
        if (drawn.Mesh != shape)
        {
            drawn.Mesh = shape;
        }

        var length = 2 * size;
        var stretch = flat
            ? Matrix4x4F.Scaling(0.5f * size * projection.M22 / projection.M11, 0.5f * size, length)
            : Matrix4x4F.Scaling(length / projection.M11, length / projection.M22, length);

        var right = (Vector3F)seen.Right;
        var up = -(Vector3F)seen.Up;
        var forward = (Vector3F)seen.Forward;
        var turn = new Matrix4x4F(
            right.X, right.Y, right.Z, 0,
            up.X, up.Y, up.Z, 0,
            forward.X, forward.Y, forward.Z, 0,
            0, 0, 0, 1);
        return stretch * turn;
    }

    private static Matrix4x4F TurnOf(Entity target)
    {
        var world = target.Transform.GetWorldMatrixF();
        var turn = Matrix4x4F.Identity;
        turn.Right = Vector3F.Normalize(world.Right);
        turn.Up = Vector3F.Normalize(world.Up);
        turn.Forward = Vector3F.Normalize(world.Forward);
        return turn;
    }
}
