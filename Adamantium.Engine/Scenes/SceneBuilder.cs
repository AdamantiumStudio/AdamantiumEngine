using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
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

        foreach (var entry in scene.Behaviours)
        {
            if (byId.TryGetValue(entry.On, out var entity))
            {
                AddBehaviour(entity, entry);
            }
            else
            {
                Log.Logger.Warning("Scene: {Type} is on {On}, which the scene does not hold; it is left out",
                    entry.Type, entry.OnText);
            }
        }

        target.Satellites.TryGet(out LightManager lights);
        var entities = new List<Entity>(made.Count);
        foreach (var each in made)
        {
            var entity = each.Entity;
            entities.Add(entity);
            if (lights != null && entity.ContainsComponent<Light>())
            {
                lights.AddLight(entity);
            }
            else if (entity.Owner == null)
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
                new EntityImportTemplate(data, assets, Vector3<double>.Zero).Build(entity);
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "Scene: the model {Asset} of {Name} could not be made", entry.Asset, entry.Name);
            }
        }

        entity.Name = entry.Name;
        entity.Source = entry.Asset;
        Place(entity, entry);
        entity.Transform.ScaleFactor = entry.Scale;
        return entity;
    }

    private static Entity MakeLight(SceneLight entry)
    {
        var entity = new Entity(null, entry.Name, entry.Uid);
        Place(entity, entry);
        var type = entry.Type is LightType.Directional or LightType.Point or LightType.Spot ? entry.Type : LightType.Point;
        var light = new LightTemplate().Build(entity, type);
        light.Color = entry.Color;
        light.Intensity = entry.Intensity;
        light.Range = entry.Range;
        light.InnerSpotAngle = float.DegreesToRadians(entry.InnerSpotAngle);
        light.OuterSpotAngle = float.DegreesToRadians(entry.OuterSpotAngle);
        light.IsWithShadows = entry.Shadows;
        light.ShadowMapResolution = entry.ShadowMapResolution;
        return entity;
    }

    private static Entity MakeCamera(SceneCamera entry)
    {
        var entity = new Entity(null, entry.Name, entry.Uid);
        entity.Transform.Position = entry.Position;
        var camera = new CameraTemplate().Build(entity, Vector3<double>.ForwardLH, -Vector3<double>.Up,
            entry.FieldOfView, DefaultViewWidth, DefaultViewHeight, entry.Near, entry.Far);
        camera.Type = entry.Type;
        camera.ProjectionType = entry.Projection;
        camera.OrthoScale = entry.OrthoScale;
        // A camera's own rotation is its view, the opposite turn of the way it faces.
        camera.Rotation = QuaternionF.Conjugate(Rotation(entry));
        return entity;
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

    private static Entity MakeShape(SceneShape entry)
    {
        var entity = new Entity(null, entry.Name, entry.Uid);
        var type = ComponentFields.FindType<ProceduralShape>(entry.Type);
        if (type == null)
        {
            Log.Logger.Warning("Scene: the shape {Type} of {Name} is not loaded; it is kept as it was read",
                entry.Type, entry.Name);
            entity.AddComponent(new UnloadedShape(entry));
        }
        else
        {
            var shape = (ProceduralShape)Activator.CreateInstance(type);
            ComponentFields.Read(entry.Values, shape, typeof(ActivatableComponent));
            new ShapeTemplate(shape).Build(entity);
            entity.AddComponent(new Material { DiffuseColor = entry.Color });
        }

        entity.Name = entry.Name;
        Place(entity, entry);
        entity.Transform.ScaleFactor = entry.Scale;
        return entity;
    }

    private static void AddBehaviour(Entity entity, SceneBehaviour entry)
    {
        var type = ComponentFields.FindType<Behaviour>(entry.Type);
        if (type == null)
        {
            var unloaded = entity.GetComponent<UnloadedBehaviours>();
            if (unloaded == null)
            {
                unloaded = new UnloadedBehaviours();
                entity.AddComponent(unloaded);
            }

            unloaded.Entries.Add(entry);
            return;
        }

        if (entity.ContainsComponent(type))
        {
            Log.Logger.Warning("Scene: {Name} has a {Type} already, and an entity has one of each; the second is left out",
                entity.Name, entry.Type);
            return;
        }

        var behaviour = (Behaviour)Activator.CreateInstance(type);
        ComponentFields.Read(entry.Values, behaviour, typeof(Behaviour));
        entity.AddComponent(behaviour);
    }

    private static void Place(Entity entity, ScenePlacement entry)
    {
        entity.Transform.Position = entry.Position;
        entity.Transform.Rotation = Rotation(entry);
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
