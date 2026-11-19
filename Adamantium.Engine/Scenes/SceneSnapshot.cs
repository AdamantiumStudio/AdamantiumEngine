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
    private readonly SceneState _state;

    private SceneSnapshot(SceneState state)
    {
        _state = state;
    }

    /// <summary>The scene.</summary>
    public Scene Scene => _state.Scene;

    /// <summary>The state of <paramref name="scene"/> now; its changes are recorded from here.</summary>
    public static SceneSnapshot Take(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var state = SceneState.Take(scene);
        scene.Changes.Start();
        return new SceneSnapshot(state);
    }

    /// <summary>Whether <paramref name="entity"/> was in the scene when the state was taken.</summary>
    public bool Contains(Entity entity) => _state.Contains(entity);

    /// <summary>Puts the scene back as it was: an entity that joined it since is taken out - unless it, or one it stands
    /// in, is in <paramref name="keep"/>; one taken out comes back where it stood, the same object; one changed gets its
    /// state back, its game's components with it. Recording stops.</summary>
    public void Restore(IReadOnlySet<Entity> keep = null)
    {
        var changes = Scene.Changes;
        changes.Stop();
        _state.PutOn(changes.Changed, changes.Removed, changes.Added, keep);
        changes.Clear();
    }

    /// <summary>Leaves the scene as it stands and stops recording: what changed since the state was taken, with how it
    /// stood then and how it stands now, either of which <see cref="KeptChanges"/> puts back - an undo of keeping what a
    /// game did, and its redo.</summary>
    public KeptChanges KeepChanges()
    {
        var changes = Scene.Changes;
        changes.Stop();
        var kept = new KeptChanges(_state, SceneState.Take(Scene), [.. changes.Changed], [.. changes.Added],
            [.. changes.Removed]);
        changes.Clear();
        return kept;
    }
}

/// <summary>What changed in a scene between two moments - the entities changed, joined and taken out - with how the
/// scene stood at each, either put back on it. On the world's thread.</summary>
public sealed class KeptChanges
{
    private readonly SceneState _before;
    private readonly SceneState _after;
    private readonly Entity[] _changed;
    private readonly Entity[] _added;
    private readonly Entity[] _removed;

    internal KeptChanges(SceneState before, SceneState after, Entity[] changed, Entity[] added, Entity[] removed)
    {
        _before = before;
        _after = after;
        _changed = changed;
        _added = added;
        _removed = removed;
    }

    /// <summary>The scene.</summary>
    public Scene Scene => _before.Scene;

    /// <summary>The entities changed, those taken out among them.</summary>
    public IReadOnlyList<Entity> Changed => _changed;

    /// <summary>Whether anything changed, joined or was taken out.</summary>
    public bool HasChanges => _changed.Length > 0 || _added.Length > 0 || _removed.Length > 0;

    /// <summary>Puts the scene as it stood at first: what joined goes, what was taken out comes back.</summary>
    public void PutBefore() => _before.PutOn(_changed, _removed, _added, null);

    /// <summary>Puts the scene as it stood at last: what joined comes back, what was taken out goes again.</summary>
    public void PutAfter() => _after.PutOn(_changed, _added, _removed, null);
}

// How a scene stood at one moment: its file, and how each entity stood in it.
internal sealed class SceneState
{
    private readonly SceneFile _file;
    private readonly Dictionary<Entity, EntityState> _states;
    // The roots, and each entity's children, in the order they stood: siblings are put back in it.
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
        var viewCameras = ViewCameraEntities(scene);
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

    // Puts this state on the scene: those of comingBack it held come back where they stood, the same objects; each of
    // changed - and those coming back, with what stands in them - gets its state back, its game's components with it;
    // those of going it did not hold go, unless one of keep holds them.
    public void PutOn(IReadOnlyCollection<Entity> changed, IReadOnlyCollection<Entity> comingBack,
        IReadOnlyCollection<Entity> going, IReadOnlySet<Entity> keep)
    {
        var manager = Scene.World.EntityManager;
        var toPut = new HashSet<Entity>(changed);
        var returned = ReturnRemoved(comingBack, manager, toPut);
        // Those coming back get their state too, with what stands in them: it may have changed while they were out.
        AddWithWhatTheyHold(toPut, returned);
        Dictionary<UInt128, ScenePlacement> entries = null;
        Dictionary<UInt128, List<SceneGameComponent>> components = null;
        var code = Scene.World.Satellites.Get<IWorldCode>();
        var problems = new List<GameComponentProblem>();
        // The entities whose children, and whether the scene's roots, are to be put in order again; none for the roots.
        var reorder = new HashSet<Entity>();
        var reorderRoots = false;
        foreach (var entity in toPut)
        {
            if (!_states.TryGetValue(entity, out var state))
                continue;

            entries ??= EntriesById();
            if (entries.TryGetValue(entity.Uid, out var entry))
            {
                SceneBuilder.Apply(entity, entry);
                components ??= GameComponentsById();
                SceneBuilder.ApplyGameComponents(entity, components.GetValueOrDefault(entity.Uid), code, problems);
            }

            // Last: the file places an entity standing in a model's part as it stands, not as it hangs. One coming back
            // is hung back by the return, among siblings put in order after.
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

        // After those hung under them are hung back: they do not go with them. One whose owner goes too goes with it.
        var viewCameras = ViewCameraEntities(Scene);
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

    // Owners first: one taken out of an entity taken out after it comes back once that one has. One hung under an entity
    // of no scene was not taken out: it is hung back as it stood, among those changed.
    private HashSet<Entity> ReturnRemoved(IReadOnlyCollection<Entity> removed, EntityManager manager, HashSet<Entity> changed)
    {
        var toReturn = new List<Entity>(removed.Count);
        foreach (var entity in removed)
        {
            if (!_states.ContainsKey(entity))
                continue;

            // Hung outside, or back in the scene somewhere else: put where it stood, not returned.
            var hungOutside = entity.Owner != null && entity.Scene == null;
            var backInScene = entity.Scene == Scene && !manager.IsBeingRemoved(entity) && manager.GetSiblingIndex(entity) >= 0;
            if (hungOutside || backInScene)
                changed.Add(entity);
            else
                toReturn.Add(entity);
        }

        toReturn.Sort(ByPlace);
        foreach (var entity in toReturn)
        {
            var state = _states[entity];
            manager.ReturnEntity(entity, state.Owner, Scene, state.Index);
        }

        return [.. toReturn];
    }

    // Every child that stood under an entity of reorder - and every root, when reorderRoots - goes to its place, in the
    // order they stood: each put at its own index lands there, whatever was moved around it.
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

    // Adds entities and, from the state, all that stood in them.
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

    private int ByPlace(Entity first, Entity second)
    {
        var byDepth = DepthOf(first).CompareTo(DepthOf(second));
        return byDepth != 0 ? byDepth : _states[first].Index.CompareTo(_states[second].Index);
    }

    private int DepthOf(Entity entity)
    {
        var depth = 0;
        for (var owner = _states[entity].Owner; owner != null && _states.TryGetValue(owner, out var state); owner = state.Owner)
        {
            depth++;
        }

        return depth;
    }

    // The entities of the views' own cameras: they stand among the scene's roots, but are the views', not the scene's.
    private static HashSet<Entity> ViewCameraEntities(Scene scene)
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

    private Dictionary<UInt128, ScenePlacement> EntriesById()
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

    private Dictionary<UInt128, List<SceneGameComponent>> GameComponentsById()
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

        // Gives whether it stood elsewhere - under another entity, or at another place among its siblings: its siblings
        // are put in order after.
        public bool ApplyTo(Entity entity, EntityManager manager, bool hangBack)
        {
            var moved = false;
            if (hangBack)
            {
                moved = entity.Owner != Owner || manager.GetSiblingIndex(entity) != Index;
                if (entity.Owner != Owner)
                    manager.SetOwner(entity, Owner);
            }

            if (entity.Transform.State != Transform)
                entity.Transform.State = Transform;

            entity.Name = Name;
            entity.IsEnabled = IsEnabled;
            entity.Visible = Visible;
            return moved;
        }
    }
}
