using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Multiverse;
using Serilog;

namespace Adamantium.Engine.Scenes;

/// <summary>
/// Writes what stands in a world into a <see cref="SceneFile"/>: a model's root - an entity naming its asset
/// (<see cref="Entity.Source"/>) - as the model, where and which way, nothing of its parts; a light, a camera, a spawn
/// point whole; the game's components on any of them. The cameras the universe's outputs own are the editor's view, not
/// the scene's, and are left out, as is whatever else stands in the world.
/// </summary>
public static class SceneCapture
{
    public static SceneFile Capture(EntityWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var ownCameras = new HashSet<Camera>();
        if (world.Satellites.TryGet(out IUniverse universe))
        {
            foreach (var output in universe.Outputs)
            {
                if (output.OwnCamera != null) ownCameras.Add(output.OwnCamera);
            }
        }

        var scene = new SceneFile();
        var stack = new Stack<(Entity Entity, Entity Placed)>();
        var roots = world.EntityManager.RootEntities;
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            stack.Push((roots[i], null));
        }

        while (stack.Count > 0)
        {
            var (entity, placed) = stack.Pop();
            var entry = Entry(entity, ownCameras);
            if (entry != null)
            {
                if (placed != null)
                {
                    if (entity.Owner != placed)
                    {
                        Log.Logger.Warning(
                            "Scene: {Name} stands in a part of {Placed}; the scene puts it in {Placed} itself",
                            entity.Name, placed.Name);
                    }

                    entry.Parent = placed.Uid;
                }

                Add(scene, entry);
                AddBehaviours(scene, entity);
                placed = entity;
            }

            for (var i = entity.Dependencies.Count - 1; i >= 0; i--)
            {
                stack.Push((entity.Dependencies[i], placed));
            }
        }

        return scene;
    }

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
                InnerSpotAngle = float.RadiansToDegrees(light.InnerSpotAngle),
                OuterSpotAngle = float.RadiansToDegrees(light.OuterSpotAngle),
                Shadows = light.IsWithShadows, ShadowMapResolution = light.ShadowMapResolution
            };
        }
        else if (entity.GetComponent<Camera>() is { } camera)
        {
            if (ownCameras.Contains(camera)) return null;

            var placedCamera = new SceneCamera
            {
                Projection = camera.ProjectionType, FieldOfView = camera.Fov, Near = camera.ZNear, Far = camera.ZFar,
                OrthoScale = camera.OrthoScale, Follows = camera.LookAtObject?.Uid
            };
            placedCamera.Uid = entity.Uid;
            placedCamera.Name = entity.Name;
            placedCamera.Position = entity.Transform.Position;
            placedCamera.Rotation = camera.Rotation;
            return placedCamera;
        }
        else if (entity.ContainsComponent<SpawnPoint>())
        {
            entry = new SceneSpawnPoint();
        }
        else
        {
            return null;
        }

        entry.Uid = entity.Uid;
        entry.Name = entity.Name;
        entry.Position = entity.Transform.Position;
        entry.Rotation = entity.Transform.Rotation;
        return entry;
    }

    private static void Add(SceneFile scene, ScenePlacement entry)
    {
        switch (entry)
        {
            case SceneModel model: scene.Models.Add(model); break;
            case SceneLight light: scene.Lights.Add(light); break;
            case SceneCamera camera: scene.Cameras.Add(camera); break;
            case SceneSpawnPoint point: scene.SpawnPoints.Add(point); break;
        }
    }

    private static void AddBehaviours(SceneFile scene, Entity entity)
    {
        foreach (var component in entity.Components)
        {
            switch (component)
            {
                case UnloadedBehaviour unloaded:
                    unloaded.Entry.On = entity.Uid;
                    scene.Behaviours.Add(unloaded.Entry);
                    break;
                case Behaviour behaviour:
                    var entry = new SceneBehaviour { On = entity.Uid, Type = behaviour.GetType().FullName };
                    BehaviourFields.Write(behaviour, entry);
                    scene.Behaviours.Add(entry);
                    break;
            }
        }
    }
}
