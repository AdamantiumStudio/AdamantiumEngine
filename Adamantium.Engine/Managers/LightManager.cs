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
/// The lights of one scene, one of its <see cref="Scene.Satellites"/>: what the outputs showing it are lit by - the
/// scene's <see cref="Light"/> components, read from its component index as the frame's outputs settle. A light added
/// meanwhile lights the scene from the next frame; one under a disabled entity lights nothing.
/// </summary>
public class LightManager
{
    private readonly IUniverse _universe;
    private readonly Scene _scene;
    private readonly EntityManager _entities;
    private readonly List<Light> _found = [];
    private readonly HashSet<Light> _foundSet = [];
    private readonly List<Light> _ordered = [];
    private readonly Dictionary<Light, LightType> _listed = new();
    private readonly MeshData _spotLightRenderer;
    private readonly MeshData _pointLightRenderer;

    /// <summary>The lights of <paramref name="scene"/>, put among its satellites.</summary>
    public LightManager(IUniverse universe, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(universe);
        ArgumentNullException.ThrowIfNull(scene);
        _universe = universe;
        _scene = scene;
        _entities = scene.World.EntityManager;
        _spotLightRenderer = new SpotLightMeshTemplate().BuildEntity().GetComponent<MeshData>();
        _pointLightRenderer = new PointLightMeshTemplate().BuildEntity().GetComponent<MeshData>();
        scene.Satellites.Add(this);
        if (scene.IsRemoved)
            return;

        universe.OutputsSettled += OnOutputsSettled;
        scene.Removed += OnSceneRemoved;
    }

    /// <summary>Every light, as the current frame sees it.</summary>
    public IReadOnlyList<Light> Lights { get; private set; } = [];

    public IReadOnlyList<Light> DirectionalLights { get; private set; } = [];

    public IReadOnlyList<Light> PointLights { get; private set; } = [];

    public IReadOnlyList<Light> SpotLights { get; private set; } = [];

    public Entity CreateLight(WorldPosition position, LightType type, string name)
    {
        if (string.IsNullOrEmpty(name))
            name = type.ToString();

        var light = new LightTemplate().BuildEntity(null, name, type);
        light.Transform.Position = position;
        AddLight(light);
        return light;
    }

    /// <summary>Puts <paramref name="light"/> in the scene, as adding it to the world does; an entity without a
    /// <see cref="Light"/>, or standing in another entity, is ignored.</summary>
    public void AddLight(Entity light)
    {
        if (light.GetComponent<Light>() != null && light.Owner == null)
            _entities.AddEntity(light, _scene);
    }

    /// <summary>Takes <paramref name="light"/> out of the world, as removing it does.</summary>
    public void RemoveLight(Entity light) => _entities.RemoveEntity(light);

    /// <summary>Whether <paramref name="light"/>'s light is among the current frame's <see cref="Lights"/>.</summary>
    public bool Contains(Entity light) => light?.GetComponent<Light>() is { } component && _listed.ContainsKey(component);

    public void DrawPointLightMesh(GraphicsDevice device, MeshGeometryCache geometryCache, AppTime appTime)
    {
        geometryCache.DrawMesh(device, _pointLightRenderer);
    }

    public void DrawSpotLightMesh(GraphicsDevice device, MeshGeometryCache geometryCache, AppTime appTime)
    {
        geometryCache.DrawMesh(device, _spotLightRenderer);
    }

    private void OnSceneRemoved(object sender, EventArgs e)
    {
        _scene.Removed -= OnSceneRemoved;
        _universe.OutputsSettled -= OnOutputsSettled;
    }

    private void OnOutputsSettled(object sender, EventArgs e)
    {
        _found.Clear();
        foreach (var light in _scene.Components.Of<Light>())
            _found.Add(light);

        if (IsListed())
            return;

        OrderAsListed();
        var all = _ordered.ToArray();
        Lights = all;
        DirectionalLights = Array.FindAll(all, light => light.Type == LightType.Directional);
        PointLights = Array.FindAll(all, light => light.Type == LightType.Point);
        SpotLights = Array.FindAll(all, light => light.Type == LightType.Spot);
        _listed.Clear();
        foreach (var light in all)
            _listed[light] = light.Type;
    }

    private bool IsListed()
    {
        if (_found.Count != _listed.Count)
            return false;

        foreach (var light in _found)
        {
            if (!_listed.TryGetValue(light, out var type) || type != light.Type)
                return false;
        }

        return true;
    }

    // Renderers take the first lights up to their cap: the ones kept stay in their order, the new ones come after.
    private void OrderAsListed()
    {
        _foundSet.Clear();
        foreach (var light in _found)
            _foundSet.Add(light);

        _ordered.Clear();
        var listed = Lights;
        for (int i = 0; i < listed.Count; i++)
        {
            if (_foundSet.Contains(listed[i]))
                _ordered.Add(listed[i]);
        }

        foreach (var light in _found)
        {
            if (!_listed.ContainsKey(light))
                _ordered.Add(light);
        }
    }
}
