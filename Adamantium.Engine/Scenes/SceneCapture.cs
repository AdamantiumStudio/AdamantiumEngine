using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Serilog;

namespace Adamantium.Engine.Scenes;

/// <summary>
/// Writes what stands in a scene of a world into a <see cref="SceneFile"/>, with the scene's id: a model's root - an entity naming its asset
/// (<see cref="Entity.Source"/>) - as the model, where and which way, nothing of its parts; a light, a camera, a spawn
/// point, a group or an empty entity, a procedural shape by its recipe, whole; the game's components on any of them.
/// What stands inside something the scene does not keep - a model's part - is written in the nearest entry it stands
/// in, placed as it stands. The cameras the universe's outputs own are the editor's view, not the scene's, and are left
/// out, as is whatever else stands in the scene, and what was taken out and leaves with the next frame. Called on the
/// world's thread, after the world took in what was added.
/// </summary>
public static class SceneCapture
{
    // Degrees written to this many decimals: turned to radians and back in floats, a whole angle comes back a hair off.
    private const int AngleDecimals = 4;

    /// <summary>Whether the scene keeps <paramref name="entity"/> as an entry of its own - a model's root, a light, a
    /// camera, a spawn point, a group, an empty entity or a shape - and so keeps the game's components on it; a model's
    /// part is not kept.</summary>
    public static bool Keeps(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Entry(entity, []) != null;
    }

    public static SceneFile Capture(Scene source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var ownCameras = new HashSet<Camera>();
        if (source.World.Satellites.TryGet(out IUniverse universe))
        {
            foreach (var output in universe.Outputs)
            {
                if (output.OwnCamera != null) ownCameras.Add(output.OwnCamera);
            }
        }

        var scene = new SceneFile { Uid = source.Uid };
        var stack = new Stack<Visit>();
        var roots = source.RootEntities;
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            stack.Push(new Visit(roots[i], null));
        }

        var entities = source.World.EntityManager;
        while (stack.Count > 0)
        {
            var visit = stack.Pop();
            var entity = visit.Entity;
            var placed = visit.Placed;

            // Taken out and leaving with the next frame - as a view's own camera, its view gone - with its tree.
            if (entities.IsBeingRemoved(entity))
            {
                continue;
            }

            var entry = Entry(entity, ownCameras);
            if (entry != null)
            {
                entry.Parent = placed?.Uid;
                if (entity.Owner != placed)
                {
                    PlaceIn(entry, entity, placed);
                }

                Add(scene, entry);
                AddGameComponents(scene, entity);
                placed = entity;
            }
            else if (entity.ContainsComponent<GameComponent>() || entity.ContainsComponent<UnloadedGameComponents>())
            {
                Log.Logger.Warning("Scene: {Name} is nothing the scene keeps; the game's components on it are not kept",
                    entity.Name);
            }

            for (var i = entity.Dependencies.Count - 1; i >= 0; i--)
            {
                stack.Push(new Visit(entity.Dependencies[i], placed));
            }
        }

        return scene;
    }

    // An entity still to write, and the nearest entry it stands in; null for none.
    private readonly record struct Visit(Entity Entity, Entity Placed);

    private static ScenePlacement Entry(Entity entity, HashSet<Camera> ownCameras)
    {
        ScenePlacement entry;
        if (!string.IsNullOrEmpty(entity.Source))
        {
            entry = new SceneModel { Asset = entity.Source, Scale = entity.Transform.ScaleFactor };
        }
        else if (entity.GetComponent<Light>() is { } light)
        {
            entry = new SceneLight
            {
                Type = light.Type, Color = light.Color, Intensity = light.Intensity, Range = light.Range,
                InnerSpotAngle = Degrees(light.InnerSpotAngle), OuterSpotAngle = Degrees(light.OuterSpotAngle),
                Shadows = light.IsWithShadows, ShadowMapResolution = light.ShadowMapResolution
            };
        }
        else if (entity.GetComponent<Camera>() is { } camera)
        {
            if (ownCameras.Contains(camera)) return null;

            var placedCamera = new SceneCamera
            {
                Uid = entity.Uid, Name = entity.Name ?? "", Position = entity.Transform.Position,
                // A camera's own rotation is its view, the opposite turn of the way it faces.
                Rotation = QuaternionF.Conjugate(camera.Rotation),
                Type = camera.Type, Projection = camera.ProjectionType, FieldOfView = camera.Fov,
                Near = camera.ZNear, Far = camera.ZFar, OrthoScale = camera.OrthoScale
            };
            return placedCamera;
        }
        else if (entity.ContainsComponent<SpawnPoint>())
        {
            entry = new SceneSpawnPoint();
        }
        else if (entity.GetComponent<ProceduralShape>() is { } shape)
        {
            var placedShape = new SceneShape
            {
                Type = shape.GetType().FullName,
                Color = entity.GetComponent<Material>()?.DiffuseColor ?? Material.DefaultDiffuseColor,
                Scale = entity.Transform.ScaleFactor
            };
            ComponentFields.Write(shape, typeof(ActivatableComponent), placedShape.Values, SceneShape.OwnAttributes);
            entry = placedShape;
        }
        else if (entity.GetComponent<UnloadedShape>() is { } unloaded)
        {
            var kept = new SceneShape
            {
                Type = unloaded.Entry.Type, Color = unloaded.Entry.Color, Scale = entity.Transform.ScaleFactor
            };
            kept.Values.AddRange(unloaded.Entry.Values);
            entry = kept;
        }
        else if (entity.ContainsComponent<Marker>())
        {
            entry = new SceneMarker
            {
                Kind = entity.IconKey == EntityIconKeys.Group ? SceneMarkerKind.Group : SceneMarkerKind.Empty
            };
        }
        else
        {
            return null;
        }

        entry.Uid = entity.Uid;
        entry.Name = entity.Name ?? "";
        entry.Position = entity.Transform.Position;
        entry.Rotation = entity.Transform.Rotation;
        return entry;
    }

    // Places the entry in placed - null for the scene itself - as the entity stands, through the entities between them
    // that the scene does not keep.
    private static void PlaceIn(ScenePlacement entry, Entity entity, Entity placed)
    {
        var position = entry.Position;
        var rotation = entry.Rotation;
        for (var each = entity.Owner; each != null && each != placed; each = each.Owner)
        {
            var transform = each.Transform;
            var scaled = position * transform.Scale.As<double>();
            position = Vector3.Transform(scaled, transform.Rotation.As<double>()) + transform.Position;
            rotation = transform.Rotation * rotation;
        }

        entry.Position = position;
        entry.Rotation = rotation;
    }

    private static float Degrees(float radians) => MathF.Round(float.RadiansToDegrees(radians), AngleDecimals);

    private static void Add(SceneFile scene, ScenePlacement entry)
    {
        switch (entry)
        {
            case SceneModel model: scene.Models.Add(model); break;
            case SceneLight light: scene.Lights.Add(light); break;
            case SceneCamera camera: scene.Cameras.Add(camera); break;
            case SceneSpawnPoint point: scene.SpawnPoints.Add(point); break;
            case SceneMarker marker: scene.Markers.Add(marker); break;
            case SceneShape shape: scene.Shapes.Add(shape); break;
        }
    }

    private static void AddGameComponents(SceneFile scene, Entity entity)
    {
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case UnloadedGameComponents unloaded:
                    foreach (var kept in unloaded.Entries)
                    {
                        scene.GameComponents.Add(kept.CopyOn(entity.Uid));
                    }

                    break;
                case GameComponent gameComponent:
                    scene.GameComponents.Add(SceneGameComponents.Write(gameComponent, entity.Uid));
                    break;
            }
        }
    }
}
