using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Multiverse;

namespace Adamantium.Engine.Scenes;

/// <summary>A scene's state at one moment, to put it back later: what the scene keeps, as its file has it, and how each
/// entity stands - placed, enabled, shown, under which entity and where - which the file does not all keep. Taking it
/// starts recording the scene's changes (<see cref="Scene.Changes"/>); putting it back undoes only what changed, so its
/// cost follows the changes, not the scene's size. On the world's thread.</summary>
public sealed class SceneSnapshot
{
    private readonly SceneFile _file;
    private readonly Dictionary<Entity, EntityState> _states;

    private SceneSnapshot(Scene scene, SceneFile file, Dictionary<Entity, EntityState> states)
    {
        Scene = scene;
        _file = file;
        _states = states;
    }

    /// <summary>The scene.</summary>
    public Scene Scene { get; }

    /// <summary>The state of <paramref name="scene"/> now; its changes are recorded from here.</summary>
    public static SceneSnapshot Take(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var file = SceneCapture.Capture(scene);
        var states = new Dictionary<Entity, EntityState>();
        var stack = new Stack<Entity>();
        var roots = scene.RootEntities;
        var views = ViewCameras(scene);
        for (var i = 0; i < roots.Count; i++)
        {
            if (views.Contains(roots[i]))
                continue;

            states[roots[i]] = EntityState.Of(roots[i], null, i);
            stack.Push(roots[i]);
        }

        while (stack.Count > 0)
        {
            var owner = stack.Pop();
            var parts = owner.Dependencies;
            for (var i = 0; i < parts.Count; i++)
            {
                states[parts[i]] = EntityState.Of(parts[i], owner, i);
                stack.Push(parts[i]);
            }
        }

        scene.Changes.Start();
        return new SceneSnapshot(scene, file, states);
    }

    /// <summary>Whether <paramref name="entity"/> was in the scene when the state was taken.</summary>
    public bool Holds(Entity entity) => _states.ContainsKey(entity);

    /// <summary>Puts the scene back as it was: an entity that joined it since is taken out - unless it, or one it stands
    /// in, is in <paramref name="keep"/>; one taken out comes back where it stood, the same object; one touched gets its
    /// state back, its game's components with it. Recording stops.</summary>
    public void Restore(IReadOnlySet<Entity> keep = null)
    {
        var changes = Scene.Changes;
        changes.Stop();
        var manager = Scene.World.EntityManager;
        var touched = new HashSet<Entity>(changes.Touched);
        var returned = ReturnRemoved(changes.Removed, manager, touched);
        Dictionary<UInt128, ScenePlacement> entries = null;
        Dictionary<UInt128, List<SceneGameComponent>> components = null;
        var code = Scene.World.Satellites.Get<IWorldCode>();
        var problems = new List<GameComponentProblem>();
        foreach (var entity in touched)
        {
            if (!_states.TryGetValue(entity, out var state))
                continue;

            entries ??= EntriesById();
            if (entries.TryGetValue(entity.Uid, out var entry))
            {
                SceneBuilder.Apply(entity, entry);
                components ??= ComponentsById();
                SceneBuilder.ApplyGameComponents(entity, components.GetValueOrDefault(entity.Uid), code, problems);
            }

            // Last: the file places an entity standing in a model's part as it stands, not as it hangs. One coming back
            // comes to its place with the return.
            state.ApplyTo(entity, manager, !returned.Contains(entity));
        }

        // After those hung under them in the game are hung back: they do not go with them.
        var views = ViewCameras(Scene);
        foreach (var entity in changes.Added)
        {
            if (!_states.ContainsKey(entity) && !Kept(entity, keep) && !views.Contains(entity))
                manager.RemoveEntity(entity);
        }

        changes.Clear();
    }

    // Owners first: one taken out of an entity taken out after it comes back once that one has. One hung under an entity
    // of no scene was not taken out: it is hung back as it stood, among those touched.
    private HashSet<Entity> ReturnRemoved(IReadOnlyCollection<Entity> removed, EntityManager manager, HashSet<Entity> touched)
    {
        var back = new List<Entity>(removed.Count);
        foreach (var entity in removed)
        {
            if (!_states.ContainsKey(entity))
                continue;

            // Hung outside, or back in the scene somewhere else: put where it stood, not returned.
            if (entity.Owner != null && entity.Scene == null || entity.Scene == Scene && !manager.IsBeingRemoved(entity))
                touched.Add(entity);
            else
                back.Add(entity);
        }

        // Siblings in the order they stood, so each goes back to its own place.
        back.Sort((first, second) =>
        {
            var byDepth = Depth(first).CompareTo(Depth(second));
            return byDepth != 0 ? byDepth : _states[first].Index.CompareTo(_states[second].Index);
        });
        foreach (var entity in back)
        {
            var state = _states[entity];
            manager.ReturnEntity(entity, state.Owner, Scene, state.Index);
        }

        return [.. back];
    }

    private int Depth(Entity entity)
    {
        var depth = 0;
        for (var owner = _states[entity].Owner; owner != null && _states.TryGetValue(owner, out var state); owner = state.Owner)
        {
            depth++;
        }

        return depth;
    }

    // The entities of the views' own cameras: they stand among the scene's roots, but are the views', not the scene's.
    private static HashSet<Entity> ViewCameras(Scene scene)
    {
        var views = new HashSet<Entity>();
        if (scene.World.Satellites.TryGet(out IUniverse universe))
        {
            foreach (var output in universe.Outputs)
            {
                if (output.OwnCamera?.Owner is { } camera)
                    views.Add(camera);
            }
        }

        return views;
    }

    private static bool Kept(Entity entity, IReadOnlySet<Entity> keep)
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

    private Dictionary<UInt128, ScenePlacement> EntriesById()
    {
        var byId = new Dictionary<UInt128, ScenePlacement>();
        Add(byId, _file.Models);
        Add(byId, _file.Lights);
        Add(byId, _file.Cameras);
        Add(byId, _file.SpawnPoints);
        Add(byId, _file.Markers);
        Add(byId, _file.Shapes);
        return byId;
    }

    private static void Add<T>(Dictionary<UInt128, ScenePlacement> byId, List<T> section) where T : ScenePlacement
    {
        foreach (var entry in section)
        {
            byId[entry.Uid] = entry;
        }
    }

    private Dictionary<UInt128, List<SceneGameComponent>> ComponentsById()
    {
        var byId = new Dictionary<UInt128, List<SceneGameComponent>>();
        foreach (var entry in _file.GameComponents)
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

    // What the scene's file does not all keep of an entity.
    private readonly record struct EntityState(
        string Name, TransformState Transform, bool IsEnabled, bool Visible, Entity Owner, int Index)
    {
        public static EntityState Of(Entity entity, Entity owner, int index) =>
            new(entity.Name, entity.Transform.State, entity.IsEnabled, entity.Visible, owner, index);

        public void ApplyTo(Entity entity, EntityManager manager, bool place)
        {
            if (place && entity.Owner != Owner)
            {
                manager.SetOwner(entity, Owner);
                manager.PlaceAt(entity, Index);
            }
            else if (place && manager.GetSiblingIndex(entity) != Index)
            {
                manager.PlaceAt(entity, Index);
            }

            if (entity.Transform.State != Transform)
                entity.Transform.State = Transform;

            entity.Name = Name;
            entity.IsEnabled = IsEnabled;
            entity.Visible = Visible;
        }
    }
}
