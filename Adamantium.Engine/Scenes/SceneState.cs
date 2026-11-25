using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.Multiverse;

namespace Adamantium.Engine.Scenes;

internal sealed class SceneState
{
    private readonly SceneFile _file;
    private readonly Dictionary<Entity, EntityState> _states;
    private readonly List<Entity> _roots;
    private readonly Dictionary<Entity, Entity[]> _children;

    private SceneState(Scene scene, SceneFile file, Dictionary<Entity, EntityState> states, List<Entity> roots,
        Dictionary<Entity, Entity[]> children)
    {
        Scene = scene;
        _file = file;
        _states = states;
        _roots = roots;
        _children = children;
    }

    public Scene Scene { get; }

    public static SceneState Take(Scene scene)
    {
        var file = SceneCapture.Capture(scene);
        var states = new Dictionary<Entity, EntityState>();
        var roots = new List<Entity>();
        var children = new Dictionary<Entity, Entity[]>();
        var stack = new Stack<Entity>();
        var sceneRoots = scene.RootEntities;
        var viewCameras = GetViewCameraEntities(scene);
        for (var i = 0; i < sceneRoots.Count; i++)
        {
            if (viewCameras.Contains(sceneRoots[i]))
                continue;

            states[sceneRoots[i]] = EntityState.Of(sceneRoots[i], null, i);
            roots.Add(sceneRoots[i]);
            stack.Push(sceneRoots[i]);
        }

        while (stack.Count > 0)
        {
            var owner = stack.Pop();
            var parts = owner.Dependencies;
            if (parts.Count == 0)
                continue;

            var ownChildren = new Entity[parts.Count];
            for (var i = 0; i < parts.Count; i++)
            {
                ownChildren[i] = parts[i];
                states[parts[i]] = EntityState.Of(parts[i], owner, i);
                stack.Push(parts[i]);
            }

            children[owner] = ownChildren;
        }

        return new SceneState(scene, file, states, roots, children);
    }

    public bool Contains(Entity entity) => _states.ContainsKey(entity);

    public void RenameLayer(string from, string to) => _file.RenameLayer(from, to);

    public bool NamesLayer(string layer) => _file.NamesLayer(layer);

    public void PutOn(IReadOnlyCollection<Entity> changed, IReadOnlyCollection<Entity> comingBack,
        IReadOnlyCollection<Entity> going, IReadOnlySet<Entity> keep)
    {
        var manager = Scene.World.EntityManager;
        var toPut = new HashSet<Entity>(changed);
        var returned = ReturnRemoved(comingBack, manager, toPut);
        AddWithWhatTheyHold(toPut, returned);
        Dictionary<UInt128, ScenePlacement> entries = null;
        Dictionary<UInt128, Entity> entities = null;
        Dictionary<UInt128, List<SceneComponent>> components = null;
        var code = Scene.World.Satellites.Get<IWorldCode>();
        var problems = new List<GameComponentProblem>();
        var reorder = new HashSet<Entity>();
        var reorderRoots = false;
        foreach (var entity in toPut)
        {
            if (!_states.TryGetValue(entity, out var state))
                continue;

            entries ??= GetEntriesById();
            if (entries.TryGetValue(entity.Uid, out var entry))
            {
                SceneBuilder.Apply(entity, entry);
                if (entry.Joints is { Count: > 0 } ||entity.GetComponent<ECS.Components.Joint>() != null)
                {
                    entities ??= GetEntitiesById();
                    SceneBuilder.ApplyJoints(entity, entry, entities);
                }
                components ??= GetComponentsById();
                SceneBuilder.ApplyComponents(entity, components.GetValueOrDefault(entity.Uid), code, problems);
            }

            var isComingBack = returned.Contains(entity);
            if (state.ApplyTo(entity, manager, hangBack: !isComingBack) || isComingBack)
            {
                if (state.Owner == null)
                    reorderRoots = true;
                else
                    reorder.Add(state.Owner);
            }
        }

        PutInOrder(reorder, reorderRoots, manager);

        var viewCameras = GetViewCameraEntities(Scene);
        var leaving = new HashSet<Entity>();
        foreach (var entity in going)
        {
            if (!_states.ContainsKey(entity) && !IsKept(entity, keep) && !viewCameras.Contains(entity))
                leaving.Add(entity);
        }

        foreach (var entity in leaving)
        {
            if (!GoesWithOwner(entity, leaving))
                manager.RemoveEntity(entity);
        }
    }

    private static bool GoesWithOwner(Entity entity, HashSet<Entity> leaving)
    {
        for (var owner = entity.Owner; owner != null; owner = owner.Owner)
        {
            if (leaving.Contains(owner))
                return true;
        }

        return false;
    }

    private HashSet<Entity> ReturnRemoved(IReadOnlyCollection<Entity> removed, EntityManager manager, HashSet<Entity> changed)
    {
        var toReturn = new List<Entity>(removed.Count);
        foreach (var entity in removed)
        {
            if (!_states.ContainsKey(entity))
                continue;

            var hungOutside = entity.Owner != null && entity.Scene == null;
            var backInScene = entity.Scene == Scene && !manager.IsBeingRemoved(entity) && manager.GetSiblingIndex(entity) >= 0;
            if (hungOutside || backInScene)
                changed.Add(entity);
            else
                toReturn.Add(entity);
        }

        toReturn.Sort(CompareByPlace);
        foreach (var entity in toReturn)
        {
            var state = _states[entity];
            manager.ReturnEntity(entity, state.Owner, Scene, state.Index);
        }

        return [.. toReturn];
    }

    private void PutInOrder(HashSet<Entity> reorder, bool reorderRoots, EntityManager manager)
    {
        if (reorderRoots)
            PlaceInOrder(_roots, manager);

        foreach (var owner in reorder)
        {
            if (_children.TryGetValue(owner, out var ownChildren))
                PlaceInOrder(ownChildren, manager);
        }
    }

    private void PlaceInOrder(IReadOnlyList<Entity> siblings, EntityManager manager)
    {
        foreach (var sibling in siblings)
        {
            manager.PlaceAt(sibling, _states[sibling].Index);
        }
    }

    private void AddWithWhatTheyHold(HashSet<Entity> into, IEnumerable<Entity> entities)
    {
        var stack = new Stack<Entity>(entities);
        while (stack.Count > 0)
        {
            var entity = stack.Pop();
            into.Add(entity);
            if (!_children.TryGetValue(entity, out var ownChildren))
                continue;

            foreach (var child in ownChildren)
            {
                stack.Push(child);
            }
        }
    }

    private int CompareByPlace(Entity first, Entity second)
    {
        var byDepth = GetDepth(first).CompareTo(GetDepth(second));
        return byDepth != 0 ? byDepth : _states[first].Index.CompareTo(_states[second].Index);
    }

    private int GetDepth(Entity entity)
    {
        var depth = 0;
        for (var owner = _states[entity].Owner; owner != null && _states.TryGetValue(owner, out var state); owner = state.Owner)
        {
            depth++;
        }

        return depth;
    }

    private static HashSet<Entity> GetViewCameraEntities(Scene scene)
    {
        var entities = new HashSet<Entity>();
        if (scene.World.Satellites.TryGet(out IUniverse universe))
        {
            foreach (var output in universe.Outputs)
            {
                if (output.OwnCamera?.Owner is { } camera)
                    entities.Add(camera);
            }
        }

        return entities;
    }

    private static bool IsKept(Entity entity, IReadOnlySet<Entity> keep)
    {
        if (keep == null)
            return false;

        for (var current = entity; current != null; current = current.Owner)
        {
            if (keep.Contains(current))
                return true;
        }

        return false;
    }

    // The scene's entities kept, by id: what a joint put back holds to.
    private Dictionary<UInt128, Entity> GetEntitiesById()
    {
        var byId = new Dictionary<UInt128, Entity>(_states.Count);
        foreach (var entity in _states.Keys)
        {
            byId[entity.Uid] = entity;
        }

        return byId;
    }

    private Dictionary<UInt128, ScenePlacement> GetEntriesById()
    {
        var byId = new Dictionary<UInt128, ScenePlacement>();
        AddEntries(byId, _file.Models);
        AddEntries(byId, _file.Lights);
        AddEntries(byId, _file.Cameras);
        AddEntries(byId, _file.SpawnPoints);
        AddEntries(byId, _file.Markers);
        AddEntries(byId, _file.Shapes);
        return byId;
    }

    private static void AddEntries<T>(Dictionary<UInt128, ScenePlacement> byId, List<T> section) where T : ScenePlacement
    {
        foreach (var entry in section)
        {
            byId[entry.Uid] = entry;
        }
    }

    private Dictionary<UInt128, List<SceneComponent>> GetComponentsById()
    {
        var byId = new Dictionary<UInt128, List<SceneComponent>>();
        foreach (var entry in _file.Components)
        {
            if (!byId.TryGetValue(entry.On, out var list))
            {
                list = [];
                byId[entry.On] = list;
            }

            list.Add(entry);
        }

        return byId;
    }
}
