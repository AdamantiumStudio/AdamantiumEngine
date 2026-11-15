using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.Engine.Templates.Lights;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.Rendering;
using Adamantium.Graphics;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.Multiverse;

namespace Adamantium.Engine.Managers;

/// <summary>
/// The lights of one scene, one of its <see cref="Scene.Satellites"/>: what the outputs showing it are lit by. It follows
/// the world: every light that comes into the scene - on its own, inside a model, put back by an undo - is listed, and
/// every one that leaves is not, whatever brought it in or took it out. The lists stand as they were when the frame's
/// outputs settled: a light added meanwhile lights the scene from the next frame.
/// </summary>
public class LightManager
{
    private readonly IUniverse universe;
    private readonly Scene scene;
    private readonly EntityManager entities;
    private readonly List<Light> lights = [];
    private readonly object syncObject = new();
    private readonly EntityGroup lightsGroup = new("Lights");
    private readonly MeshData spotLightRenderer;
    private readonly MeshData pointLightRenderer;
    private bool lightsDirty = true;

    /// <summary>The lights of <paramref name="scene"/>, put among its satellites; the lights already in it are listed.
    /// </summary>
    public LightManager(IUniverse universe, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(scene);
        this.universe = universe;
        this.scene = scene;
        entities = scene.World.EntityManager;
        spotLightRenderer = new SpotLightMeshTemplate().BuildEntity().GetComponent<MeshData>();
        pointLightRenderer = new PointLightMeshTemplate().BuildEntity().GetComponent<MeshData>();
        universe.OutputsSettled += OnOutputsSettled;
        entities.EntityAdded += OnEntityAdded;
        entities.EntityRemoved += OnEntityRemoved;
        scene.Removed += OnSceneRemoved;
        scene.Satellites.Add(this);
        foreach (var root in scene.RootEntities)
        {
            Take(root);
        }
    }

    private void OnSceneRemoved(object sender, EventArgs e)
    {
        scene.Removed -= OnSceneRemoved;
        universe.OutputsSettled -= OnOutputsSettled;
        entities.EntityAdded -= OnEntityAdded;
        entities.EntityRemoved -= OnEntityRemoved;
    }

    /// <summary>Every light, as the current frame sees it.</summary>
    public IReadOnlyList<Light> Lights { get; private set; } = [];

    public IReadOnlyList<Light> DirectionalLights { get; private set; } = [];

    public IReadOnlyList<Light> PointLights { get; private set; } = [];

    public IReadOnlyList<Light> SpotLights { get; private set; } = [];

    public Entity CreateLight(Vector3<double> position, LightType type, string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            name = type.ToString();
        }

        var light = new LightTemplate().BuildEntity(null, name, type);
        light.Transform.Position = position;
        AddLight(light);
        return light;
    }

    /// <summary>Puts <paramref name="light"/> in the scene, as adding it to the world does; an entity without a
    /// <see cref="Light"/> is ignored, and one standing in another entity joins the scene with that entity's root.
    /// </summary>
    public void AddLight(Entity light)
    {
        if (light.GetComponent<Light>() != null && light.Owner == null)
        {
            entities.AddEntity(light, scene);
        }
    }

    /// <summary>Takes <paramref name="light"/> out of the world, as removing it does: it leaves the lights with it.
    /// </summary>
    public void RemoveLight(Entity light) => entities.RemoveEntity(light);

    public bool Contains(Entity light)
    {
        return lightsGroup.Contains(light);
    }

    public void DrawPointLightMesh(GraphicsDevice device, MeshGeometryCache geometryCache, AppTime appTime)
    {
        geometryCache.DrawMesh(device, pointLightRenderer);
    }

    public void DrawSpotLightMesh(GraphicsDevice device, MeshGeometryCache geometryCache, AppTime appTime)
    {
        geometryCache.DrawMesh(device, spotLightRenderer);
    }

    private void OnEntityAdded(object sender, EntityEventArgs e)
    {
        if (e.Entity.Scene == scene)
        {
            Take(e.Entity);
        }
    }

    private void OnEntityRemoved(object sender, EntityEventArgs e) => Drop(e.Entity);

    private void Take(Entity tree)
    {
        tree.TraverseInDepth(entity =>
        {
            if (entity.GetComponent<Light>() is not { } light || lightsGroup.Contains(entity))
                return;

            lightsGroup.Add(entity);
            lock (syncObject)
            {
                lights.Add(light);
                lightsDirty = true;
            }
        });
    }

    private void Drop(Entity tree)
    {
        tree.TraverseInDepth(entity =>
        {
            if (!lightsGroup.Contains(entity))
                return;

            lightsGroup.Remove(entity);
            lock (syncObject)
            {
                lights.Remove(entity.GetComponent<Light>());
                lightsDirty = true;
            }
        });
    }

    private void OnOutputsSettled(object sender, EventArgs e)
    {
        lock (syncObject)
        {
            if (!lightsDirty)
            {
                return;
            }

            lightsDirty = false;
            var all = lights.ToArray();
            Lights = all;
            DirectionalLights = Array.FindAll(all, light => light.Type == LightType.Directional);
            PointLights = Array.FindAll(all, light => light.Type == LightType.Point);
            SpotLights = Array.FindAll(all, light => light.Type == LightType.Spot);
        }
    }
}
