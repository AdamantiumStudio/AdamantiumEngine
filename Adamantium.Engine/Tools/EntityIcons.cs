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
using Adamantium.Multiverse;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Icons for what a scene has but does not draw - lights, cameras, and a <see cref="Marker"/> (a group among them) or
/// <see cref="SpawnPoint"/> with no mesh on it or under it, whatever else it holds - facing every
/// camera at a steady size on screen. A directional light's icon also has rays running where it shines, a camera's the
/// shape of its view - a pyramid or, flat, a box - running where it looks, with a mark on top. A click on an icon selects
/// its entity. A camera has no icon in its own view, and an output's own camera (<see cref="UniverseOutput.OwnCamera"/>)
/// none at all: it is the output's, not the scene's.
/// </summary>
public class EntityIcons : ToolsProcessor
{
    /// <summary>The size icons have unless set otherwise, in points.</summary>
    public const float DefaultPixels = 36;

    private readonly Dictionary<Scene, Entity> roots = new();
    private readonly List<Scene> scenesGone = [];
    private Entity collectingInto;
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
    // Whether an entity is seen by a mesh, worked out once a frame; the walk's lists are kept so it allocates nothing.
    private readonly Dictionary<Entity, bool> lookThisFrame = new();
    private readonly Stack<Entity> looking = new();
    private readonly List<Entity> walked = new();
    private readonly Dictionary<Entity, Entity> targetOf = new();
    private readonly Dictionary<Entity, Entity> raysOf = new();
    private readonly Dictionary<Entity, Entity> _lookOf = new();
    private readonly HashSet<Entity> seen = [];
    private readonly List<Entity> gone = [];

    /// <summary>How big an icon looks, in points: pixels at 100% scale.</summary>
    public float Pixels { get; set; } = DefaultPixels;

    /// <summary>The kinds of icon shown; an entity of a kind left out has none, and a click cannot pick it by one.
    /// </summary>
    public EntityIconKinds Shows { get; set; } = EntityIconKinds.All;

    public override void Update(AppTime appTime)
    {
        // Each scene shown has icons of its own, which only the outputs showing it draw.
        seen.Clear();
        lookThisFrame.Clear();
        foreach (var scene in Tools.Observatory.ShownScenes)
        {
            collectingInto = RootOf(scene);
            if (Shows.HasFlag(EntityIconKinds.Lights))
                CollectOwnersOf<Light>(scene);

            if (Shows.HasFlag(EntityIconKinds.Cameras))
                CollectOwnersOf<Camera>(scene);

            if ((Shows & (EntityIconKinds.Entities | EntityIconKinds.Groups)) != 0)
                CollectOwnersOf<Marker>(scene);

            if (Shows.HasFlag(EntityIconKinds.Entities))
                CollectOwnersOf<SpawnPoint>(scene);
        }

        collectingInto = null;
        DropGone();
        DropRemovedScenes();

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
    public static void PlaceIcon(Entity icon, WorldPosition at, float pixels, CameraBase camera)
    {
        var size = pixels * ScreenSpace.UnitsPerPoint(camera, at);
        var metadata = icon.Transform.GetMetadata(camera);
        metadata.WorldMatrixF = Matrix4x4.CreateScale<float>(size, -size, size)
                                * Matrix4x4.CreateFromQuaternion<float>(ScreenSpace.Facing(camera))
                                * Matrix4x4.CreateTranslation<float>(ScreenSpace.InRender(at, camera));
        metadata.Enabled = true;
    }

    public override void DrawOverlay(ToolsOverlayProcessor overlay)
    {
        if (overlay.Scene is { } scene && roots.TryGetValue(scene, out var icons))
        {
            overlay.DrawFlatInScene(icons);
        }
    }

    /// <summary>The icon that stands for <paramref name="target"/> in the scene, or null when it shows none.</summary>
    public Entity IconOf(Entity target) => target != null && iconOf.TryGetValue(target, out var icon) ? icon : null;

    public override PickHit PickEntity(in PickRay ray)
    {
        if (Tools.Scene is not { } scene || !roots.TryGetValue(scene, out var icons))
        {
            return default;
        }

        var hit = icons.Pick(ray, PickMode.MeshColliders);
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
            if (target == camera.Owner || target.Scene != Tools.Scene)
            {
                continue;
            }

            var at = target.GetComponent<Camera>()?.WorldPosition ?? target.Transform.WorldPosition;
            var inRender = ScreenSpace.InRender(at, camera);
            if (Vector3F.Dot(inRender, camera.Forward.As<float>()) <= camera.ZNear && camera.ProjectionMatrix.M44 < 0.5f)
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

    private void CollectOwnersOf<T>(Scene scene) where T : class, IComponent, IEntityOwner
    {
        foreach (var component in scene.Components.Of<T>())
            Collect(component.Owner);
    }

    private void Collect(Entity entity)
    {
        if (seen.Contains(entity))
            return;

        var look = LookOf(entity);
        if (look == null)
        {
            return;
        }

        seen.Add(entity);
        if (_lookOf.TryGetValue(entity, out var shownLook) && shownLook != look)
            DropIcon(entity);

        if (iconOf.TryGetValue(entity, out var icon))
        {
            // Gone over to another scene: its icon goes with it.
            if (icon.Owner != collectingInto)
            {
                Tools.EntityWorld.EntityManager.SetOwner(icon, collectingInto);
            }
        }
        else
        {
            icon = Instance(collectingInto, look);
            icon.AddComponent(new BoxCollider());
            iconOf[entity] = icon;
            targetOf[icon] = entity;
            _lookOf[entity] = look;
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
        // Hidden in the hierarchy - itself or an owner - it is hidden in the view: no icon pops up in its place.
        if (IsHidden(entity))
        {
            return null;
        }

        // An entity is of one kind: a light of a hidden kind does not fall through to the icon of its marker.
        if (entity.GetComponent<Light>() is { } light)
        {
            if (!Shows.HasFlag(EntityIconKinds.Lights))
            {
                return null;
            }

            return light.Type switch
            {
                LightType.Point => pointIcon,
                LightType.Spot => spotIcon,
                _ => directionalIcon
            };
        }

        if (entity.GetComponent<Camera>() is { } camera && !IsOwnCamera(camera))
        {
            return Shows.HasFlag(EntityIconKinds.Cameras) ? cameraIcon : null;
        }

        if (!IsMarked(entity))
        {
            return null;
        }

        var group = entity.GetComponent<Marker>() != null && entity.IconKey == EntityIconKeys.Group;
        var kind = group ? EntityIconKinds.Groups : EntityIconKinds.Entities;
        if (!Shows.HasFlag(kind))
        {
            return null;
        }

        // A group too: what its meshes show is read in the scene, an icon over them only hides it - unless asked for
        // (EditorDisplay.ShowsIcon), to take the entity by.
        var asked = entity.GetComponent<EditorDisplay>() is { IsEnabled: true, ShowsIcon: true };
        if (!asked && HasLook(entity))
        {
            return null;
        }

        return group ? groupIcon : emptyIcon;
    }

    private static bool IsHidden(Entity entity)
    {
        for (var each = entity; each != null; each = each.Owner)
        {
            if (!each.Visible)
            {
                return true;
            }
        }

        return false;
    }

    // Whether a mesh the view draws is on the entity or under it; each entity is looked at once a frame.
    private bool HasLook(Entity entity)
    {
        if (lookThisFrame.TryGetValue(entity, out var known))
        {
            return known;
        }

        if (IsShown(entity) && DrawsOwnMesh(entity))
        {
            lookThisFrame[entity] = true;
            return true;
        }

        // Down the tree, then back up it: an entity is answered after everything under it. The first mesh seen on the way
        // down answers for the whole tree at once - a group of thousands is not walked to its end.
        looking.Push(entity);
        while (looking.TryPop(out var next))
        {
            if (lookThisFrame.TryGetValue(next, out var answered))
            {
                if (answered)
                {
                    return SeenAtOnce(entity);
                }

                continue;
            }

            walked.Add(next);
            if (!IsShown(next))
            {
                continue;
            }

            if (DrawsOwnMesh(next))
            {
                return SeenAtOnce(entity);
            }

            foreach (var under in next.Dependencies.AsSpan())
            {
                if (under != null)
                {
                    looking.Push(under);
                }
            }
        }

        for (var i = walked.Count - 1; i >= 0; i--)
        {
            var next = walked[i];
            var seen = IsShown(next) && (DrawsOwnMesh(next) || AnySeenUnder(next));
            lookThisFrame[next] = seen;
        }

        walked.Clear();
        return lookThisFrame[entity];
    }

    // A mesh found under the entity: it is seen; the entities passed on the way are answered when asked.
    private bool SeenAtOnce(Entity entity)
    {
        looking.Clear();
        walked.Clear();
        lookThisFrame[entity] = true;
        return true;
    }

    // What the renderer and picking leave out - an entity turned off or hidden, and all under it - shows nothing.
    private static bool IsShown(Entity entity) => entity.IsEnabled && entity.Visible;

    private static bool DrawsOwnMesh(Entity entity) =>
        entity.GetComponent<MeshData>() is { IsEnabled: true, Mesh.HasPoints: true };

    private bool AnySeenUnder(Entity entity)
    {
        foreach (var under in entity.Dependencies.AsSpan())
        {
            if (under != null && lookThisFrame.TryGetValue(under, out var seen) && seen)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the entity is one shown by an icon of its own and taken by it: a marked entity, or a spawn point
    /// with no marker turned off.</summary>
    public static bool IsMarked(Entity entity) =>
        entity?.GetComponent<Marker>() is { } marker ? marker.IsEnabled : entity?.GetComponent<SpawnPoint>() != null;

    /// <summary>Whether <see cref="EditorDisplay.ShowsIcon"/> changes anything for the entity: a marked one whose meshes
    /// may hide its icon - not a light or a camera, which always show theirs.</summary>
    public static bool CanAskForIcon(Entity entity) =>
        IsMarked(entity) && !entity.ContainsComponent<Light>() && !entity.ContainsComponent<Camera>();

    private bool IsOwnCamera(Camera camera)
    {
        var outputs = Tools.Observatory.Outputs;
        for (int i = 0; i < outputs.Count; i++)
        {
            if (outputs[i].OwnCamera == camera)
            {
                return true;
            }
        }

        return false;
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
            DropIcon(gone[i]);
    }

    private void DropIcon(Entity target)
    {
        if (!iconOf.Remove(target, out var icon))
            return;

        targetOf.Remove(icon);
        raysOf.Remove(target);
        _lookOf.Remove(target);
        Tools.EntityWorld.EntityManager.RemoveOwner(icon);
    }

    private Entity RootOf(Scene scene)
    {
        if (!roots.TryGetValue(scene, out var icons))
        {
            icons = new Entity(null, $"{nameof(EntityIcons)} of {scene.Name}");
            roots[scene] = icons;
        }

        return icons;
    }

    private void DropRemovedScenes()
    {
        scenesGone.Clear();
        foreach (var scene in roots.Keys)
        {
            if (scene.IsRemoved)
            {
                scenesGone.Add(scene);
            }
        }

        foreach (var scene in scenesGone)
        {
            roots.Remove(scene);
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
                : Matrix4x4.CreateScale<float>(size) * TurnOf(target);
            raysMetadata.WorldMatrixF = stretchAndTurn * Matrix4x4.CreateTranslation<float>(ScreenSpace.InRender(at, camera));
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
            ? Matrix4x4.CreateScale<float>(0.5f * size * projection.M22 / projection.M11, 0.5f * size, length)
            : Matrix4x4.CreateScale<float>(length / projection.M11, length / projection.M22, length);

        var right = seen.Right.As<float>();
        var up = -seen.Up.As<float>();
        var forward = seen.Forward.As<float>();
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
