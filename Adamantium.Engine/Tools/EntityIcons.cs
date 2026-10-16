using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Templates.CameraTemplates;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Icons for what a scene has but does not draw - lights and cameras - facing every camera at a steady size on screen. A
/// directional light's icon also has rays running where it shines. A click on an icon selects its entity. A camera has
/// no icon in its own view.
/// </summary>
public class EntityIcons : EditorProcessor
{
    private readonly Entity root = new(null, nameof(EntityIcons));
    private readonly Entity pointIcon = new PointLightIconTemplate().BuildEntity(null, "Point light");
    private readonly Entity spotIcon = new SpotLightIconTemplate().BuildEntity(null, "Spot light");
    private readonly Entity directionalIcon = new DirectionalLightIconTemplate().BuildEntity(null, "Directional light");
    private readonly Entity directionalRays = new DirectionalLightVisualTemplate().BuildEntity(null, "Rays");
    private readonly Entity cameraIcon = new CameraIconTemplate().BuildEntity(null, "Camera");
    private readonly Dictionary<Entity, Entity> iconOf = new();
    private readonly Dictionary<Entity, Entity> targetOf = new();
    private readonly Dictionary<Entity, Entity> raysOf = new();
    private readonly HashSet<Entity> seen = [];
    private readonly List<Entity> gone = [];

    /// <summary>How big an icon looks, in points: pixels at 100% scale.</summary>
    public float Pixels { get; set; } = 36;

    public bool ShowsLights { get; set; } = true;

    public bool ShowsCameras { get; set; } = true;

    public override void Update(AppTime appTime)
    {
        seen.Clear();
        var roots = Tools.EntityWorld.RootEntities;
        for (int i = 0; i < roots.Count; i++)
        {
            roots[i].TraverseInDepth(Collect, true);
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

    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        overlay.DrawFlatInScene(root);
    }

    public override PickHit PickEntity(in PickRay ray)
    {
        var hit = root.Pick(ray, PickMode.MeshColliders);
        if (!hit.IsHit || !targetOf.TryGetValue(hit.Entity, out var target))
        {
            return default;
        }

        return new PickHit(target, hit.Point, hit.Depth);
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
        }

        icon.IsSelected = entity.IsSelected;
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

        return ShowsCameras && entity.GetComponent<Camera>() != null ? cameraIcon : null;
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
            Tools.EntityWorld.EntityManager.SetOwner(icon, null);
        }
    }

    private void Place(Entity target, Entity icon, Camera camera)
    {
        var metadata = icon.Transform.GetMetadata(camera);
        if (target == camera.Owner)
        {
            metadata.Enabled = false;
            return;
        }

        var at = target.Transform.WorldPosition;
        var size = Pixels * ScreenSpace.UnitsPerPoint(camera, at);
        metadata.WorldMatrixF = Matrix4x4F.Scaling(size, -size, size)
                                * Matrix4x4F.RotationQuaternion(ScreenSpace.Facing(camera))
                                * Matrix4x4F.Translation(ScreenSpace.InRender(at, camera));
        metadata.Enabled = true;

        if (raysOf.TryGetValue(target, out var rays))
        {
            var raysMetadata = rays.Transform.GetMetadata(camera);
            raysMetadata.WorldMatrixF = Matrix4x4F.Scaling(size)
                                        * TurnOf(target)
                                        * Matrix4x4F.Translation(ScreenSpace.InRender(at, camera));
            raysMetadata.Enabled = true;
        }
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
