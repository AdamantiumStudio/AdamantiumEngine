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
/// The lights of a universe. A light is added and removed here rather than through the entity manager, and the lists
/// stand as they were when the frame's outputs settled: a light added meanwhile lights the scene from the next frame.
/// </summary>
public class LightManager
{
    private readonly EntityWorld entityWorld;
    private readonly List<Light> lights = [];
    private readonly object syncObject = new();
    private readonly EntityGroup lightsGroup = new("Lights");
    private readonly MeshData spotLightRenderer;
    private readonly MeshData pointLightRenderer;
    private bool lightsDirty = true;

    public LightManager(IUniverse universe, EntityWorld entityWorld)
    {
        this.entityWorld = entityWorld;
        entityWorld.EntityManager.AddGroup(lightsGroup);
        spotLightRenderer = new SpotLightMeshTemplate().BuildEntity().GetComponent<MeshData>();
        pointLightRenderer = new PointLightMeshTemplate().BuildEntity().GetComponent<MeshData>();
        universe.OutputsSettled += OnOutputsSettled;
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

    /// <summary>Puts <paramref name="light"/> in the world as a light; an entity without a <see cref="Light"/> is ignored.</summary>
    public void AddLight(Entity light)
    {
        var component = light.GetComponent<Light>();
        if (component == null)
        {
            return;
        }

        lightsGroup.Add(light);
        lock (syncObject)
        {
            lights.Add(component);
            lightsDirty = true;
        }

        entityWorld.EntityManager.AddEntity(light);
    }

    /// <summary>Takes <paramref name="light"/> out of the lights and out of the world.</summary>
    public void RemoveLight(Entity light)
    {
        lightsGroup.Remove(light);
        var component = light.GetComponent<Light>();
        if (component != null)
        {
            lock (syncObject)
            {
                lights.Remove(component);
                lightsDirty = true;
            }
        }

        entityWorld.EntityManager.RemoveEntity(light);
    }

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
