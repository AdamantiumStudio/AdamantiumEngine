using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Physics;
using Adamantium.Engine.Managers;
using Adamantium.Engine.Templates;
using Adamantium.Engine.Templates.Lights;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Serilog;

namespace Adamantium.Engine.Scenes;

/// <summary>
/// Puts a <see cref="SceneFile"/> into a scene of a world: every entry an entity with the entry's id, the models loaded by the
/// asset service - all at once, each asset once however many times it stands in the scene - lights, cameras, spawn
/// points, groups, empty entities and shapes made as the file describes them, the game's components set on what they
/// are on. A model that does not load stays as an empty entity naming its asset, so writing the scene back keeps it.
/// The file may come from anywhere - a mod, say: an entry with an id another entry of the file or an entity of the world
/// already has is logged and left out, a rotation that is no rotation is taken as none.
/// </summary>
public sealed class SceneBuilder
{
    private const int DefaultViewWidth = 800;
    private const int DefaultViewHeight = 600;

    private readonly Scene target;
    private readonly EntityWorld world;
    private readonly IAssetService assets;

    /// <summary>Builds into <paramref name="target"/>: its lights are its <see cref="LightManager"/>'s, if it has one.
    /// </summary>
    public SceneBuilder(Scene target, IAssetService assets)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(assets);
        this.target = target;
        world = target.World;
        this.assets = assets;
    }

    /// <summary>The game's components the last build could not make - their code threw; their entries are kept.</summary>
    public IReadOnlyList<GameComponentProblem> Problems { get; private set; } = [];

    /// <summary>Loads the models of <paramref name="scene"/>, then builds it: for a world no frame is running in. A
    /// running universe loads with <see cref="LoadModelsAsync"/> anywhere and builds on its own thread.</summary>
    public async Task<IReadOnlyList<Entity>> Build(SceneFile scene) => Build(scene, await LoadModelsAsync(scene));

    /// <summary>The models <paramref name="scene"/> stands, by asset name, each loaded once, all at once; null for one
    /// that did not load. From any thread.</summary>
    public async Task<IReadOnlyDictionary<string, SceneData>> LoadModelsAsync(SceneFile scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var loads = new Dictionary<string, Task<SceneData>>(StringComparer.Ordinal);
        foreach (var model in scene.Models)
        {
            if (!string.IsNullOrEmpty(model.Asset) && !loads.ContainsKey(model.Asset))
            {
                loads[model.Asset] = assets.LoadAsync<SceneData>(model.Asset);
            }
        }

        var loaded = new Dictionary<string, SceneData>(StringComparer.Ordinal);
        foreach (var (asset, load) in loads)
        {
            try
            {
                loaded[asset] = await load;
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "Scene: the model {Asset} did not load", asset);
                loaded[asset] = null;
            }
        }

        return loaded;
    }

    /// <summary>Builds <paramref name="scene"/> with its <paramref name="models"/> loaded already and adds it to the
    /// world; the entities made for its entries, section by section in the file's order. On the world's thread, between
    /// its frames: it looks at what the world holds to keep ids apart.</summary>
    public IReadOnlyList<Entity> Build(SceneFile scene, IReadOnlyDictionary<string, SceneData> models)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(models);

        var byId = new Dictionary<UInt128, Entity>();
        var made = new List<Made>();
        var inWorld = WorldIds();
        var problems = new List<GameComponentProblem>();
        Problems = problems;

        foreach (var model in Admitted(scene.Models, byId, inWorld)) Keep(model, MakeModel(model, models), byId, made);
        foreach (var light in Admitted(scene.Lights, byId, inWorld)) Keep(light, MakeLight(light), byId, made);
        foreach (var camera in Admitted(scene.Cameras, byId, inWorld)) Keep(camera, MakeCamera(camera), byId, made);
        foreach (var point in Admitted(scene.SpawnPoints, byId, inWorld)) Keep(point, MakeSpawnPoint(point), byId, made);
        foreach (var marker in Admitted(scene.Markers, byId, inWorld)) Keep(marker, MakeMarker(marker), byId, made);
        foreach (var shape in Admitted(scene.Shapes, byId, inWorld)) Keep(shape, MakeShape(shape), byId, made);

        foreach (var each in made)
        {
            var entry = each.Entry;
            var entity = each.Entity;
            if (entry.Parent is not { } parent) continue;

            if (byId.TryGetValue(parent, out var owner) && !IsWithin(owner, entity))
            {
                world.EntityManager.SetOwner(entity, owner);
            }
            else
            {
                Log.Logger.Warning("Scene: {Name} stands in {Parent}, which the scene does not hold; it stands in the scene",
                    entry.Name, entry.ParentText);
            }
        }

        // Once every entry is made: a joint holds to an entry of any section.
        foreach (var each in made)
        {
            if (each.Entry.Joints is { Count: > 0 })
                ApplyJoints(each.Entity, each.Entry, byId);
        }

        foreach (var entry in scene.Components)
        {
            if (byId.TryGetValue(entry.On, out var entity))
            {
                SceneComponents.Attach(entity, entry, world.Satellites.Get<IWorldCode>(), problems);
            }
            else
            {
                Log.Logger.Warning("Scene: {Type} is on {On}, which the scene does not hold; it is left out",
                    entry.Type, entry.OnText);
            }
        }

        var entities = new List<Entity>(made.Count);
        foreach (var each in made)
        {
            var entity = each.Entity;
            entities.Add(entity);
            if (entity.Owner == null)
            {
                world.EntityManager.AddEntity(entity, target);
            }
        }

        return entities;
    }

    // The ids of every entity of the world - in every scene, ids are the world's - roots and what stands in them.
    private HashSet<UInt128> WorldIds()
    {
        var ids = new HashSet<UInt128>();
        var stack = new Stack<Entity>();
        foreach (var each in world.Scenes)
        {
            foreach (var root in each.RootEntities)
            {
                stack.Push(root);
            }
        }

        while (stack.Count > 0)
        {
            var entity = stack.Pop();
            ids.Add(entity.Uid);
            foreach (var part in entity.Dependencies)
            {
                stack.Push(part);
            }
        }

        return ids;
    }

    // The entries of a section whose ids no entry before them and no entity of the world has.
    private static List<T> Admitted<T>(List<T> section, Dictionary<UInt128, Entity> byId, HashSet<UInt128> inWorld)
        where T : ScenePlacement
    {
        var admitted = new List<T>(section.Count);
        var ids = new HashSet<UInt128>();
        foreach (var entry in section)
        {
            if (byId.ContainsKey(entry.Uid) || !ids.Add(entry.Uid))
            {
                Log.Logger.Warning("Scene: {Name} has the id {Uid} of an entry before it; it is left out",
                    entry.Name, entry.UidText);
            }
            else if (inWorld.Contains(entry.Uid))
            {
                Log.Logger.Warning("Scene: {Name} has the id {Uid} of an entity the world has already; it is left out",
                    entry.Name, entry.UidText);
            }
            else
            {
                admitted.Add(entry);
            }
        }

        return admitted;
    }

    private static void Keep(ScenePlacement entry, Entity entity, Dictionary<UInt128, Entity> byId, List<Made> made)
    {
        byId.Add(entry.Uid, entity);
        made.Add(new Made(entry, entity));
    }

    // An entry of the file and the entity made for it.
    private readonly record struct Made(ScenePlacement Entry, Entity Entity);

    private Entity MakeModel(SceneModel entry, IReadOnlyDictionary<string, SceneData> models)
    {
        var entity = new Entity(null, entry.Name, entry.Uid) { IconKey = EntityIconKeys.Model };
        if (!string.IsNullOrEmpty(entry.Asset) && models.TryGetValue(entry.Asset, out var data) && data != null)
        {
            try
            {
                new EntityImportTemplate(data, assets, WorldPosition.Zero).Build(entity);
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "Scene: the model {Asset} of {Name} could not be made", entry.Asset, entry.Name);
            }
        }

        entity.Name = entry.Name;
        entity.Source = entry.Asset;
        ApplyModel(entity, entry);
        return entity;
    }

    /// <summary>Puts on <paramref name="entity"/>, made for <paramref name="entry"/>, what the entry says of it - its
    /// place and the settings of its kind - as building it does; the components kept by type are put back by
    /// <see cref="ApplyComponents"/>. For a scene put back as it was. On the world's thread.</summary>
    internal static void Apply(Entity entity, ScenePlacement entry)
    {
        switch (entry)
        {
            case SceneModel model:
                ApplyModel(entity, model);
                break;
            case SceneLight light when entity.GetComponent<Light>() is { } component:
                Place(entity, light);
                ApplyLight(component, light);
                break;
            case SceneCamera camera when entity.GetComponent<Camera>() is { } component:
                ApplyCamera(entity, component, camera);
                break;
            case SceneShape shape:
                ApplyShape(entity, shape);
                break;
            default:
                Place(entity, entry);
                break;
        }
    }

    /// <summary>Makes the components of <paramref name="entity"/> kept by type those of <paramref name="entries"/> - none
    /// for null: one of a kind not there is taken off, one there gets the entry's values, one missing is put on. On the
    /// world's thread.</summary>
    internal static void ApplyComponents(Entity entity, IReadOnlyList<SceneComponent> entries, IWorldCode code,
        List<GameComponentProblem> problems)
    {
        var existing = KeptComponentsOf(entity);
        foreach (var component in existing)
        {
            if (!HasEntryOfType(entries, component.GetType().FullName))
                entity.RemoveComponent(component);
        }

        if (entries == null)
            return;

        HashSet<Component> applied = null;
        var unloaded = entity.GetComponent<UnloadedComponents>();
        foreach (var entry in entries)
        {
            // Kept as it was read - its code not loaded: no game changed it, it is there still.
            if (SceneComponents.FindType(code, entry.Type) == null)
                continue;

            if (FindOfType(existing, entry.Type) is { } component && (applied ??= []).Add(component))
            {
                SceneComponents.Read(entry, component);
            }
            else if (!HasUnloadedOfType(unloaded, entry.Type))
            {
                // A second of a kind, or one whose code threw, is kept as it was read already: not again.
                SceneComponents.Attach(entity, entry, code, problems);
            }
        }
    }

    private static List<Component> KeptComponentsOf(Entity entity)
    {
        var kept = new List<Component>();
        foreach (var component in entity.Components)
        {
            if (component is Component each && SceneComponents.IsKept(each.GetType()))
                kept.Add(each);
        }

        return kept;
    }

    private static bool HasUnloadedOfType(UnloadedComponents unloaded, string type)
    {
        if (unloaded == null)
            return false;

        foreach (var entry in unloaded.Entries)
        {
            if (entry.Type == type)
                return true;
        }

        return false;
    }

    private static Component FindOfType(List<Component> components, string type)
    {
        foreach (var component in components)
        {
            if (component.GetType().FullName == type)
                return component;
        }

        return null;
    }

    private static bool HasEntryOfType(IReadOnlyList<SceneComponent> entries, string type)
    {
        if (entries == null)
            return false;

        foreach (var entry in entries)
        {
            if (entry.Type == type)
                return true;
        }

        return false;
    }

    private static void ApplyModel(Entity entity, SceneModel entry)
    {
        Place(entity, entry);
        entity.Transform.ScaleFactor = entry.Scale;
    }

    private static Entity MakeLight(SceneLight entry)
    {
        var entity = new Entity(null, entry.Name, entry.Uid);
        Place(entity, entry);
        var type = entry.Type is LightType.Directional or LightType.Point or LightType.Spot ? entry.Type : LightType.Point;
        ApplyLight(new LightTemplate().Build(entity, type), entry);
        return entity;
    }

    private static void ApplyLight(Light light, SceneLight entry)
    {
        if (entry.Type is LightType.Directional or LightType.Point or LightType.Spot)
            light.Type = entry.Type;

        light.Color = entry.Color;
        light.Intensity = entry.Intensity;
        light.Range = entry.Range;
        light.InnerSpotAngle = float.DegreesToRadians(entry.InnerSpotAngle);
        light.OuterSpotAngle = float.DegreesToRadians(entry.OuterSpotAngle);
        light.IsWithShadows = entry.Shadows;
        light.ShadowMapResolution = entry.ShadowMapResolution;
    }

    private static Entity MakeCamera(SceneCamera entry)
    {
        var entity = new Entity(null, entry.Name, entry.Uid);
        var camera = new CameraTemplate().Build(entity, Vector3<double>.ForwardLH, -Vector3<double>.Up,
            entry.FieldOfView, DefaultViewWidth, DefaultViewHeight, entry.Near, entry.Far);
        ApplyCamera(entity, camera, entry);
        return entity;
    }

    private static void ApplyCamera(Entity entity, Camera camera, SceneCamera entry)
    {
        entity.Transform.Position = new WorldPosition(entry.Position);
        entity.IsEnabled = entry.IsEnabled;
        camera.Fov = entry.FieldOfView;
        camera.ZNear = entry.Near;
        camera.ZFar = entry.Far;
        camera.Type = entry.Type;
        camera.ProjectionType = entry.Projection;
        camera.OrthoScale = entry.OrthoScale;
        // A camera's own rotation is its view, the opposite turn of the way it faces.
        camera.Rotation = QuaternionF.Conjugate(Rotation(entry));
    }

    private static Entity MakeSpawnPoint(SceneSpawnPoint entry)
    {
        var entity = new Entity(null, entry.Name, entry.Uid);
        Place(entity, entry);
        entity.AddComponent(new SpawnPoint());
        return entity;
    }

    private static Entity MakeMarker(SceneMarker entry)
    {
        var entity = new Entity(null, entry.Name, entry.Uid)
        {
            IconKey = entry.Kind == SceneMarkerKind.Group ? EntityIconKeys.Group : EntityIconKeys.Entity
        };
        Place(entity, entry);
        entity.AddComponent(new Marker());
        return entity;
    }

    private Entity MakeShape(SceneShape entry)
    {
        var entity = new Entity(null, entry.Name, entry.Uid);
        var shape = MakeShapeComponent(entry);
        if (shape == null)
        {
            entity.AddComponent(new UnloadedShape(entry));
        }
        else
        {
            new ShapeTemplate(shape).Build(entity);
            entity.AddComponent(new Material());
        }

        entity.Name = entry.Name;
        ApplyShape(entity, entry);
        return entity;
    }

    private static void ApplyShape(Entity entity, SceneShape entry)
    {
        entity.Transform.ScaleFactor = entry.Scale;
        if (entity.GetComponent<ProceduralShape>() is { } shape && shape.GetType().FullName == entry.Type)
            ComponentFields.Read(entry.Values, shape, typeof(ActivatableComponent));

        if (entity.GetComponent<Material>() is { } material)
        {
            material.DiffuseColor = entry.Color;
            material.AlphaMode = entry.AlphaMode;
            material.AlphaCutoff = entry.AlphaCutoff;
            material.Faces = entry.Faces;
        }

        // After the shape's values: the collider its template makes may follow them.
        Place(entity, entry);
    }

    private ProceduralShape MakeShapeComponent(SceneShape entry)
    {
        var type = world.Satellites.Get<IWorldCode>().FindType<ProceduralShape>(entry.Type);
        if (type == null)
        {
            Log.Logger.Warning("Scene: the shape {Type} of {Name} is not loaded; it is kept as it was read",
                entry.Type, entry.Name);
            return null;
        }

        try
        {
            var shape = (ProceduralShape)Activator.CreateInstance(type);
            ComponentFields.Read(entry.Values, shape, typeof(ActivatableComponent));
            return shape;
        }
        catch (Exception exception) when (exception is TargetInvocationException or TypeInitializationException)
        {
            Log.Logger.Error(exception.InnerException ?? exception,
                "Scene: the shape {Type} of {Name} could not be made; it is kept as it was read", entry.Type, entry.Name);
            return null;
        }
    }


    private static void Place(Entity entity, ScenePlacement entry)
    {
        entity.Transform.Position = new WorldPosition(entry.Position);
        entity.Transform.Rotation = Rotation(entry);
        entity.IsEnabled = entry.IsEnabled;
        if (entry is not SceneLight && entity.GetComponent<UnloadedShape>() == null)
        {
            ApplyPhysics(entity, entry);
        }
    }

    /// <summary>Makes <paramref name="entity"/>'s joints the ones <paramref name="entry"/> keeps, each held to the
    /// entity of <paramref name="byId"/> it names or to the world; joints of other kinds are taken off, but a model's
    /// root keeps the ones its model's edits give it. A kind this engine does not know, or an entity the scene does not
    /// hold, leaves that joint out, logged - the entity's joint of that kind as it is. Building, and putting a scene back
    /// as it was; on the world's thread.</summary>
    internal static void ApplyJoints(Entity entity, ScenePlacement entry, IReadOnlyDictionary<UInt128, Entity> byId)
    {
        if (entry is SceneLight or SceneCamera || entity.GetComponent<UnloadedShape>() != null)
            return;

        List<JointEdits> named = null;
        foreach (var kept in entry.Joints ?? [])
        {
            if (kept?.ToEdits() is not { } edits)
            {
                Log.Logger.Warning("Scene: a joint of {Name} is of a kind this engine does not know; it is left out",
                    entry.Name);
                continue;
            }

            // Named, it stays even when left out below: the entity keeps the joint of that kind it has.
            (named ??= []).Add(edits);
            Entity connected = null;
            if (kept.Connected is { } uid && !byId.TryGetValue(uid, out connected))
            {
                Log.Logger.Warning("Scene: a joint of {Name} holds to {Connected}, which the scene does not hold; it is "
                                   + "left out", entry.Name, kept.ConnectedText);
                continue;
            }

            PhysicsParts.SetJoint(entity, edits, connected);
        }

        if (entry is not SceneModel)
            PhysicsParts.RemoveJointsNotIn(entity, named);
    }

    // The entity's physics made the entry's - built, or put back as it was when a game stops: its body, none for none
    // (a model's root keeping the one its model's edits give it); a shape's collider, the template's for none written.
    // What already is so is left as it is.
    private static void ApplyPhysics(Entity entity, ScenePlacement entry)
    {
        var body = entity.GetComponent<RigidBody>();
        if (entry.Body is { } kept)
        {
            if (kept.ToEdits() is not { } edits)
            {
                Log.Logger.Warning("Scene: the body of {Name} is of a kind this engine does not know; it is left out",
                    entry.Name);
            }
            else if (body == null || !PhysicsParts.IsAlike(body, edits))
            {
                PhysicsParts.SetBody(entity, edits);
            }
        }
        else if (body != null && entry is not SceneModel)
        {
            entity.RemoveComponent(body);
        }

        if (entry is not SceneShape shape || entity.GetComponent<ProceduralShape>() is not { } made)
        {
            return;
        }

        ApplyShapeCollider(entity, shape, made);
        var traits = shape.Collider?.Traits is { } read && !read.HoldsDefaults() ? read : null;
        if (entity.GetComponent<Collider>() is { } settled && PhysicsParts.TraitsOf(settled) != traits)
        {
            PhysicsParts.SetTraits(settled, traits);
        }
    }

    private static void ApplyShapeCollider(Entity entity, SceneShape shape, ProceduralShape made)
    {
        // A second collider given in a game counts as other than wanted.
        var colliders = entity.GetComponents<Collider>();
        var current = colliders.Length > 1
            ? (ColliderKind?)null
            : PhysicsParts.KindOf(colliders.Length == 0 ? null : colliders[0]);
        if (shape.Collider?.Kind is { } kind)
        {
            if (current != kind)
            {
                PhysicsParts.SetCollider(entity, kind);
            }

            // Only on a collider of that kind: one kept for a mesh it could not be fitted to has sizes of its own. A
            // shape read with nothing but defaults is none.
            var wanted = shape.Collider.Shape is { } read && !read.HoldsDefaults() ? read : null;
            if (entity.GetComponent<Collider>() is { } collider && PhysicsParts.KindOf(collider) == kind
                && collider.GetShape() != wanted)
            {
                PhysicsParts.SetShape(collider, wanted);
            }

            return;
        }

        if (shape.Collider != null)
        {
            Log.Logger.Warning("Scene: the collider of {Name} is of a kind this engine does not know; it keeps the " +
                               "template's", shape.Name);
        }

        if (PhysicsParts.TemplateColliderOf(made) is not { } template)
        {
            return;
        }

        if (current != PhysicsParts.KindOf(template))
        {
            PhysicsParts.PutCollider(entity, template);
            return;
        }

        // Of the template's kind but sized otherwise - in a game: the same collider, sized as the template's.
        var templateShape = template.GetShape();
        if (colliders.Length == 1 && colliders[0].GetShape() != templateShape)
        {
            PhysicsParts.SetShape(colliders[0], templateShape);
        }
    }

    // The entry's rotation made unit; none for one that is no rotation - zero, or not numbers.
    private static QuaternionF Rotation(ScenePlacement entry)
    {
        var rotation = entry.Rotation;
        var lengthSquared = rotation.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared < 1e-12f)
        {
            Log.Logger.Warning("Scene: {Name} has the rotation {Rotation}, which is none; it faces as it is",
                entry.Name, entry.RotationText);
            return QuaternionF.Identity;
        }

        return rotation.IsNormalized ? rotation : QuaternionF.Normalize(rotation);
    }

    // Whether owner is entity or stands in it: a file whose parents go round in a circle puts no entity in itself.
    private static bool IsWithin(Entity owner, Entity entity)
    {
        for (var each = owner; each != null; each = each.Owner)
        {
            if (each == entity) return true;
        }

        return false;
    }
}
