using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Managers;
using Adamantium.Engine.Templates;
using Adamantium.Graphics.Core.Assets;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Serilog;

namespace Adamantium.Engine.Scenes;

/// <summary>
/// Puts a <see cref="SceneFile"/> into a world: every entry an entity with the entry's id, the models loaded by the
/// asset service - all at once, each asset once however many times it stands in the scene - lights, cameras and spawn
/// points made as the file describes them, the game's components set on what they are on. A model that does not load
/// stays as an empty entity naming its asset, so writing the scene back keeps it.
/// </summary>
public sealed class SceneBuilder
{
    private readonly EntityWorld world;
    private readonly IAssetService assets;

    public SceneBuilder(EntityWorld world, IAssetService assets)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(assets);
        this.world = world;
        this.assets = assets;
    }

    /// <summary>Builds <paramref name="scene"/> and adds it to the world; the entities made for its entries, in the
    /// file's order: models, lights, cameras, spawn points.</summary>
    public async Task<IReadOnlyList<Entity>> Build(SceneFile scene)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var loads = new Dictionary<string, Task<SceneData>>(StringComparer.Ordinal);
        foreach (var model in scene.Models.Where(model => !string.IsNullOrEmpty(model.Asset)))
        {
            if (!loads.ContainsKey(model.Asset))
            {
                loads[model.Asset] = assets.LoadAsync<SceneData>(model.Asset);
            }
        }

        var made = new List<(ScenePlacement Entry, Entity Entity)>();
        foreach (var model in scene.Models)
        {
            made.Add((model, await MakeModel(model, loads)));
        }

        made.AddRange(scene.Lights.Select(light => ((ScenePlacement)light, MakeLight(light))));
        made.AddRange(scene.Cameras.Select(camera => ((ScenePlacement)camera, MakeCamera(camera))));
        made.AddRange(scene.SpawnPoints.Select(point => ((ScenePlacement)point, MakeSpawnPoint(point))));

        var byId = new Dictionary<UInt128, Entity>();
        foreach (var (entry, entity) in made)
        {
            if (!byId.TryAdd(entry.Uid, entity))
            {
                Log.Logger.Warning("Scene: two entries have the id {Uid}; references find the first", entry.UidText);
            }
        }

        foreach (var (entry, entity) in made)
        {
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

        foreach (var camera in scene.Cameras)
        {
            if (camera.Follows is { } followed && byId.TryGetValue(followed, out var target))
            {
                byId[camera.Uid].GetComponent<Camera>().LookAtObject = target;
            }
        }

        foreach (var entry in scene.Behaviours)
        {
            if (!byId.TryGetValue(entry.On, out var entity))
            {
                Log.Logger.Warning("Scene: {Type} is on {On}, which the scene does not hold; it is left out",
                    entry.Type, entry.OnText);
                continue;
            }

            entity.AddComponent(MakeBehaviour(entry));
        }

        world.Satellites.TryGet(out LightManager lights);
        var entities = made.Select(pair => pair.Entity).ToList();
        foreach (var entity in entities)
        {
            if (lights != null && entity.ContainsComponent<Light>())
            {
                lights.AddLight(entity);
            }
            else if (entity.Owner == null)
            {
                world.EntityManager.AddEntity(entity);
            }
        }

        return entities;
    }

    private async Task<Entity> MakeModel(SceneModel entry, Dictionary<string, Task<SceneData>> loads)
    {
        var entity = new Entity(null, entry.Name, entry.Uid) { Source = entry.Asset, IconKey = EntityIconKeys.Model };
        if (!string.IsNullOrEmpty(entry.Asset))
        {
            try
            {
                var data = await loads[entry.Asset];
                if (data != null)
                {
                    await new EntityImportTemplate(data, assets, Vector3<double>.Zero).BuildEntity(entity);
                }
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "Scene: the model {Asset} of {Name} did not load", entry.Asset, entry.Name);
            }
        }

        entity.Name = entry.Name;
        entity.Source = entry.Asset;
        entity.Transform.Position = entry.Position;
        entity.Transform.Rotation = entry.Rotation;
        entity.Transform.ScaleFactor = entry.Scale;
        return entity;
    }

    private static Entity MakeLight(SceneLight entry)
    {
        var entity = Place(entry);
        entity.AddComponent(new Light(entry.Type)
        {
            Color = entry.Color,
            Intensity = entry.Intensity,
            Range = entry.Range,
            InnerSpotAngle = float.DegreesToRadians(entry.InnerSpotAngle),
            OuterSpotAngle = float.DegreesToRadians(entry.OuterSpotAngle),
            IsWithShadows = entry.Shadows,
            ShadowMapResolution = entry.ShadowMapResolution
        });
        return entity;
    }

    private static Entity MakeCamera(SceneCamera entry)
    {
        var entity = Place(entry);
        entity.Transform.Rotation = QuaternionF.Identity;
        entity.IconKey = EntityIconKeys.Camera;
        var camera = new Camera(Vector3<double>.ForwardLH, -Vector3<double>.Up, entry.FieldOfView, 800, 600, entry.Near,
            entry.Far)
        {
            ProjectionType = entry.Projection,
            OrthoScale = entry.OrthoScale,
            Rotation = entry.Rotation
        };
        entity.AddComponent(camera);
        return entity;
    }

    private static Entity MakeSpawnPoint(SceneSpawnPoint entry)
    {
        var entity = Place(entry);
        entity.AddComponent(new SpawnPoint());
        return entity;
    }

    private static Component MakeBehaviour(SceneBehaviour entry)
    {
        var type = BehaviourFields.FindType(entry.Type);
        if (type == null)
        {
            return new UnloadedBehaviour(entry);
        }

        var behaviour = (Behaviour)Activator.CreateInstance(type);
        BehaviourFields.Read(entry, behaviour);
        return behaviour;
    }

    private static Entity Place(ScenePlacement entry)
    {
        var entity = new Entity(null, entry.Name, entry.Uid);
        entity.Transform.Position = entry.Position;
        entity.Transform.Rotation = entry.Rotation;
        return entity;
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
