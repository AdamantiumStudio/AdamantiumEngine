using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.Collections;
using Adamantium.ECS.Components;
using Adamantium.ECS.Templates;
using Adamantium.Graphics.Core;

namespace Adamantium.ECS;

public sealed class EntityManager
{
    private object syncObject = new object();

    private readonly List<Scene> scenes;
    private readonly Dictionary<UInt128, Entity> availableEntities;
    private readonly Dictionary<String, EntityGroup> entitiesByGroup;
    private readonly List<Entity> entitiesToAdd;
    private readonly List<Entity> entitiesToRemove;
    private readonly List<Entity> branchesToRemove;
    private readonly List<Entity> entitiesReowned;
    private readonly Dictionary<Entity, Scene> entitiesMoved = new();
    private readonly List<ReturnedEntity> _entitiesReturned = [];
    private readonly Dictionary<Entity, int> _entitiesPlacedAt = new();
    private readonly HashSet<Entity> _ownersChanged = [];
    private readonly List<Entity> _following = [];
    private readonly Dictionary<UInt128, EntityParameters> entityParametersMap;
    private readonly List<CameraProjectionType> availableProjectionTypes;

    internal EntityManager(EntityWorld world)
    {
        World = world;
        MainScene = new Scene(world, nameof(MainScene), UidGenerator.Generate());
        scenes = [MainScene];
        availableEntities = new Dictionary<UInt128, Entity>();
        entitiesByGroup = new Dictionary<String, EntityGroup>();
        entitiesToAdd = new List<Entity>();
        entitiesToRemove = new List<Entity>();
        branchesToRemove = new List<Entity>();
        entitiesReowned = new List<Entity>();
        entityParametersMap = new Dictionary<UInt128, EntityParameters>();
        availableProjectionTypes = new List<CameraProjectionType>();
    }

    internal void InitializeResources()
    {
    }

    public EntityWorld World { get; }

    /// <summary>The scene the world is made with, which is never removed: the one an entity added with no scene
    /// goes to, so a world showing one scene works with it alone.</summary>
    public Scene MainScene { get; }

    /// <summary>The world's scenes, <see cref="MainScene"/> first: a copy, readable from any thread.</summary>
    public IReadOnlyList<Scene> Scenes
    {
        get
        {
            lock (syncObject)
            {
                return scenes.ToArray();
            }
        }
    }

    /// <summary>A new empty scene of the world, with an id of its own; until it is removed.</summary>
    public Scene CreateScene(string name) => CreateScene(name, UidGenerator.Generate());

    /// <summary>A new empty scene of the world with the id <paramref name="uid"/> - the one its scene file keeps; until
    /// it is removed. The world holds one scene with an id at most.</summary>
    public Scene CreateScene(string name, UInt128 uid)
    {
        var scene = new Scene(World, name, uid);
        lock (syncObject)
        {
            if (scenes.Exists(each => each.Uid == uid))
            {
                throw new InvalidOperationException($"The world has a scene with the id {uid} already.");
            }

            scenes.Add(scene);
        }

        return scene;
    }

    /// <summary>The scene with the id <paramref name="uid"/>; null if the world has none.</summary>
    public Scene FindScene(UInt128 uid)
    {
        lock (syncObject)
        {
            return scenes.Find(scene => scene.Uid == uid);
        }
    }

    /// <summary>Takes <paramref name="scene"/> out of the world at once - nothing more is added to it - and its trees
    /// with it from the next frame. <see cref="MainScene"/> is never removed.</summary>
    public void RemoveScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (scene == MainScene)
        {
            throw new InvalidOperationException("The main scene of a world is never removed.");
        }

        lock (syncObject)
        {
            if (!scenes.Remove(scene))
            {
                return;
            }

            scene.IsRemoved = true;
            foreach (var root in scene.Roots)
            {
                RemoveEntity(root);
            }
        }

        scene.RaiseRemoved();
    }

    internal void SyncEntities()
    {
        lock (syncObject)
        {
            foreach (var scene in scenes)
            {
                scene.Components.EndFrame();
            }

            if (entitiesToRemove.Count > 0)
            {
                foreach (var entity in entitiesToRemove)
                {
                    if (availableEntities.ContainsKey(entity.Uid))
                    {
                        RemoveEntityInternal(entity);
                        // Once out of its owner's scene: an owner kept would hold it in the world, and it would not
                        // come back.
                        entity.Owner = null;
                    }
                }

                entitiesToRemove.Clear();
            }

            if (branchesToRemove.Count > 0)
            {
                foreach (var branch in branchesToRemove)
                {
                    if (InWorld(branch))
                    {
                        RemoveBranch(branch);
                    }
                }

                branchesToRemove.Clear();
            }

            if (entitiesToAdd.Count > 0)
            {
                foreach (var entity in entitiesToAdd)
                {
                    AddEntityInternal(entity);
                }

                entitiesToAdd.Clear();
            }

            if (_entitiesReturned.Count > 0)
            {
                foreach (var returned in _entitiesReturned)
                {
                    ReturnEntityInternal(returned);
                }

                _entitiesReturned.Clear();
            }

            if (entitiesReowned.Count > 0)
            {
                foreach (var entity in entitiesReowned)
                {
                    PlaceByOwner(entity);
                }

                entitiesReowned.Clear();
            }

            if (_entitiesPlacedAt.Count > 0)
            {
                foreach (var placed in _entitiesPlacedAt)
                {
                    PlaceAtInternal(placed.Key, placed.Value);
                }

                _entitiesPlacedAt.Clear();
            }

            if (entitiesMoved.Count > 0)
            {
                foreach (var move in entitiesMoved)
                {
                    MoveToSceneInternal(move.Key, move.Value);
                }

                entitiesMoved.Clear();
            }

            if (_ownersChanged.Count > 0)
            {
                _following.AddRange(_ownersChanged);
                _ownersChanged.Clear();
                foreach (var entity in _following)
                {
                    FollowOwnersIndex(entity);
                }

                _following.Clear();
            }
        }
    }

    /// <summary>Notes that <paramref name="entity"/> was given another owner, from any thread: at the next sync, on the
    /// world's thread, it joins the index of components its owner is in, or leaves the one it was in. The scenes'
    /// <see cref="Scene.Changes"/> note it joined, left, or hung elsewhere.</summary>
    internal void NoteOwnerChanged(Entity entity, Scene previousScene)
    {
        var scene = entity.Scene;
        if (previousScene == null)
            scene?.Changes.NoteAdded(entity);
        else if (scene == null)
            previousScene.Changes.NoteRemoved(entity);
        else
            previousScene.Changes.NoteChanged(entity);

        lock (syncObject)
        {
            _ownersChanged.Add(entity);
        }
    }

    private void FollowOwnersIndex(Entity entity)
    {
        SceneComponents index = null;
        if (entity.Owner != null)
            index = entity.Owner.ComponentIndex;
        else if (availableEntities.ContainsKey(entity.Uid) && entity.OwnScene is { IsRemoved: false } home)
            index = home.Components;

        if (index == entity.ComponentIndex)
            return;

        entity.ComponentIndex?.Leave(entity);
        index?.Join(entity);
    }

    /// <summary>Moves the root <paramref name="root"/> with its tree to <paramref name="scene"/> of the same world, from
    /// the next frame: as an output's own camera follows the output to the scene it shows. Nothing for an entity outside
    /// the world or hanging under an owner - a branch is in its owner's scene.</summary>
    public void MoveToScene(Entity root, Scene scene)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(scene);
        if (scene.World != World)
        {
            throw new ArgumentException("The scene is another world's.", nameof(scene));
        }

        lock (syncObject)
        {
            if (scene.IsRemoved)
            {
                throw new InvalidOperationException($"The scene '{scene.Name}' is removed.");
            }

            if (entitiesToAdd.Contains(root))
            {
                root.SetScene(scene);
            }
            else if (availableEntities.ContainsKey(root.Uid))
            {
                entitiesMoved[root] = scene;
            }
        }

        root.NotifyUpdated();
    }

    private void MoveToSceneInternal(Entity root, Scene scene)
    {
        var home = root.OwnScene;
        if (!availableEntities.ContainsKey(root.Uid) || root.Owner != null || home == scene || scene.IsRemoved)
        {
            return;
        }

        home?.Roots.Remove(root);
        LeaveScene(root, home);
        root.SetScene(scene);
        scene.Roots.Add(root);
        JoinScene(root, scene);
    }

    private void AddEntityInternal(Entity entity)
    {
        // Its scene was removed before it came in: it never joins.
        if (entity.OwnScene is not { IsRemoved: false } scene)
        {
            entity.SetScene(null);
            return;
        }

        if (availableEntities.TryAdd(entity.Uid, entity))
        {
            PlaceByOwner(entity);
            JoinScene(entity, entity.Scene ?? scene);
            OnEntityAdded(entity);
        }
    }

    private static void JoinScene(Entity tree, Scene scene) => scene.Components.Join(tree);

    private static void LeaveScene(Entity tree, Scene scene) => scene?.Components.Leave(tree);

    /// <summary>Hangs <paramref name="entity"/> under <paramref name="owner"/> - with <see cref="RemoveOwner"/>, the one
    /// way an entity changes owner. A root given an owner leaves its scene's <see cref="Scene.RootEntities"/>, and moves
    /// to the owner's scene if that is another one: from the next frame, on the world's thread.</summary>
    public void SetOwner(Entity entity, Entity owner)
    {
        if (entity == null || entity.Owner == owner) return;

        lock (syncObject)
        {
            entity.Owner = owner;
            entitiesReowned.Add(entity);
        }
    }

    /// <summary>Takes <paramref name="entity"/> off its owner; one of the world's comes back among the
    /// <see cref="Scene.RootEntities"/> of the scene it was in from the next frame.</summary>
    public void RemoveOwner(Entity entity) => SetOwner(entity, null);

    // A root of its scene while it has no owner; with one it is a branch of its owner's tree, in its owner's scene.
    private void PlaceByOwner(Entity entity)
    {
        if (!availableEntities.ContainsKey(entity.Uid)) return;

        var home = entity.OwnScene;
        if (entity.Owner == null)
        {
            if (home != null && !home.Roots.Contains(entity))
            {
                home.Roots.Add(entity);
            }

            return;
        }

        home?.Roots.Remove(entity);
        if (entity.Owner.Scene is { } ownerScene && ownerScene != home)
        {
            LeaveScene(entity, home);
            entity.SetScene(ownerScene);
            JoinScene(entity, ownerScene);
        }
    }
    
    private void OnEntityAdded(Entity entity)
    {
        EntityAdded?.Invoke(this, new EntityEventArgs(entity));
    }

    private void OnEntityRemoved(Entity entity)
    {
        EntityRemoved?.Invoke(this, new EntityEventArgs(entity));
    }

    private void RemoveEntityInternal(Entity entity)
    {
        if (!availableEntities.Remove(entity.Uid)) return;

        var home = entity.OwnScene;
        var scene = entity.Scene ?? home;
        entity.SetScene(null);
        home?.Roots.Remove(entity);
        LeaveScene(entity, scene);
        OnEntityRemoved(entity);

        // Its tree goes with it: an entity of the world it holds - a camera following it - leaves the world too.
        entity.TraverseInDepth(held =>
        {
            if (held != entity) RemoveEntityInternal(held);
        });
    }

    private void RemoveBranch(Entity branch)
    {
        var scene = branch.Scene;
        branch.Owner = null;
        LeaveScene(branch, scene);
        OnEntityRemoved(branch);
        branch.TraverseInDepth(held =>
        {
            if (held != branch) RemoveEntityInternal(held);
        });
    }

    private bool InWorld(Entity entity)
    {
        for (var current = entity; current != null; current = current.Owner)
        {
            if (availableEntities.ContainsKey(current.Uid)) return true;
        }

        return false;
    }

    public Entity CreateEntity(string name, Entity owner = null, bool createDisabled = false)
    {
        lock (this)
        {
            var entity = new Entity(owner, name);

            if (createDisabled)
            {
                entity.IsEnabled = false;
            }

            return entity;
        }
    }

    /// <summary>Adds <paramref name="root"/> with its tree to <paramref name="scene"/> - to <see cref="MainScene"/> if
    /// none is given - from the next frame. Nothing for one already in the world.</summary>
    public void AddEntity(
        Entity root,
        Scene scene = null,
        CameraProjectionType projectionType = CameraProjectionType.Perspective)
    {
        if (root == null)
        {
            return;
        }

        scene ??= MainScene;
        if (scene.World != World)
        {
            throw new ArgumentException("The scene is another world's.", nameof(scene));
        }

        lock (syncObject)
        {
            if (scene.IsRemoved)
            {
                throw new InvalidOperationException($"The scene '{scene.Name}' is removed.");
            }

            if (availableEntities.ContainsKey(root.Uid)) return;

            SetCameraProjectionType(root, projectionType);
            entitiesToAdd.Add(root);
            root.SetScene(scene);
            scene.Changes.NoteAdded(root);
        }

        root.NotifyUpdated();
    }

    public void SetCameraProjectionType(Entity rootEntity, CameraProjectionType projectionType)
    {
        lock (syncObject)
        {
            entityParametersMap[rootEntity.RootUid] = new EntityParameters(projectionType);
            if (!availableProjectionTypes.Contains(projectionType))
            {
                availableProjectionTypes.Add(projectionType);
            }
        }
    }

    public CameraProjectionType GetCameraProjectionType(Entity rootEntity)
    {
        lock (syncObject)
        {
            if (entityParametersMap.TryGetValue(rootEntity.RootUid, out var value))
            {
                return value.CameraProjectionType;
            }
        }

        return CameraProjectionType.Perspective;
    }

    public CameraProjectionType[] GetAvailableCameraProjectionTypes()
    {
        return availableProjectionTypes.ToArray();
    }

    /// <summary>Whether <paramref name="entity"/>, or what it stands in, was taken out with <see cref="RemoveEntity"/>
    /// and leaves with the next frame: still in the world until then, but no longer of it.</summary>
    public bool IsBeingRemoved(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        lock (syncObject)
        {
            for (var current = entity; current != null; current = current.Owner)
            {
                if (entitiesToRemove.Contains(current) || branchesToRemove.Contains(current))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Takes <paramref name="entity"/> out of the world with its tree, from the next frame: one of the world's
    /// own, or any part of its trees - a mesh of a model. Nothing for one outside the world.</summary>
    public void RemoveEntity(Entity entity)
    {
        if (entity == null)
            return;

        lock (syncObject)
        {
            if (availableEntities.ContainsKey(entity.Uid))
            {
                if (!entitiesToRemove.Contains(entity))
                {
                    // Its own: one hung under another has the other's root.
                    entityParametersMap.Remove(entity.Uid);
                    entitiesToRemove.Add(entity);
                }
            }
            else if (InWorld(entity) && !branchesToRemove.Contains(entity))
            {
                branchesToRemove.Add(entity);
            }
        }

        entity.Scene?.Changes.NoteRemoved(entity);
        entity.NotifyUpdated();
    }

    /// <summary>Where <paramref name="entity"/> stands among its owner's entities, or among its scene's roots when it
    /// has no owner; -1 when it is in neither.</summary>
    public int GetSiblingIndex(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        lock (syncObject)
        {
            if (entity.Owner is { } owner)
            {
                return owner.Dependencies.IndexOf(entity);
            }

            return entity.OwnScene?.Roots.IndexOf(entity) ?? -1;
        }
    }

    /// <summary>Puts <paramref name="entity"/>, taken out of the world with <see cref="RemoveEntity"/>, back where it
    /// was, from the next frame: under <paramref name="owner"/> at <paramref name="index"/> among its entities, or, with
    /// no owner, among the roots of <paramref name="scene"/> at <paramref name="index"/>. The same object comes back,
    /// its components and its tree as they were. Nothing for an entity in the world already.</summary>
    public void ReturnEntity(Entity entity, Entity owner, Scene scene, int index)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (owner == null)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (scene.World != World)
            {
                throw new ArgumentException("The scene is another world's.", nameof(scene));
            }
        }

        lock (syncObject)
        {
            _entitiesReturned.Add(new ReturnedEntity(entity, owner, scene, index));
        }

        // A root taken out has no world of its own to tell.
        World.Changed(UniverseChangedEventArgs.OneFrame);
    }

    /// <summary>Moves <paramref name="entity"/> to <paramref name="index"/> among its owner's entities, or among its
    /// scene's roots when it has none - from the next frame, after the owners and roots changed in this one settle: as an
    /// entity moved under another and moved back stands where it stood.</summary>
    public void PlaceAt(Entity entity, int index)
    {
        ArgumentNullException.ThrowIfNull(entity);
        lock (syncObject)
        {
            _entitiesPlacedAt[entity] = index;
        }

        entity.NoteChanged();
    }

    private void PlaceAtInternal(Entity entity, int index)
    {
        if (entity.Owner is { } owner)
        {
            owner.MoveDependency(entity, index);
            return;
        }

        if (entity.OwnScene is { } scene && scene.Roots.IndexOf(entity) is >= 0 and var from)
        {
            var to = Math.Clamp(index, 0, scene.Roots.Count - 1);
            if (from != to)
            {
                scene.Roots.Move(from, to);
            }
        }
    }

    private void ReturnEntityInternal(ReturnedEntity returned)
    {
        var entity = returned.Entity;
        if (InWorld(entity))
        {
            return;
        }

        if (returned.Owner is { } owner)
        {
            if (!InWorld(owner))
            {
                return;
            }

            entity.Owner = owner;
            owner.MoveDependency(entity, returned.Index);
            JoinScene(entity, owner.Scene);
            OnEntityAdded(entity);
            return;
        }

        var scene = returned.Scene;
        if (scene.IsRemoved || !availableEntities.TryAdd(entity.Uid, entity))
        {
            return;
        }

        SetCameraProjectionType(entity, CameraProjectionType.Perspective);
        entity.SetScene(scene);
        scene.Roots.Insert(Math.Clamp(returned.Index, 0, scene.Roots.Count), entity);
        JoinScene(entity, scene);
        OnEntityAdded(entity);
    }

    /// <summary>Takes every root of every scene out of the world, and with each its tree; the scenes stay.</summary>
    public void RemoveAllEntities()
    {
        lock (syncObject)
        {
            foreach (var scene in scenes)
            {
                foreach (var root in scene.Roots)
                {
                    RemoveEntity(root);
                }
            }
        }
    }

    public void RemoveEntities(IEnumerable<Entity> entities)
    {
        if (entities == null)
            return;

        foreach (var entity in entities)
        {
            RemoveEntity(entity);
        }
    }

    public async Task<Entity> CreateEntityFromTemplate(
        IEntityTemplate template,
        Entity owner = null,
        string name = "",
        bool createDisabled = false)
    {
        if (template == null)
        {
            throw new ArgumentNullException(nameof(template));
        }

        var entity = CreateEntity(name, owner, createDisabled);
        await template.BuildEntity(entity);

        return entity;
    }

    public EntityGroup CreateGroup(string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
        {
            throw new ArgumentNullException(nameof(groupName));
        }

        lock (syncObject)
        {
            if (entitiesByGroup.TryGetValue(groupName, out var group))
            {
                return group;
            }

            group = new EntityGroup(groupName);
            entitiesByGroup.Add(groupName, group);
            OnGroupCreated(group);
            return group;
        }
    }

    public void AddGroup(EntityGroup group)
    {
        if (group == null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        lock (syncObject)
        {
            if (entitiesByGroup.TryAdd(group.Name, group))
            {
                OnGroupCreated(group);
            }
            else
            {
                entitiesByGroup[group.Name].Add(group);
            }
        }
    }

    public void RemoveGroup(string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
        {
            throw new ArgumentNullException(nameof(groupName));
        }

        lock (syncObject)
        {
            if (entitiesByGroup.TryGetValue(groupName, out var group))
            {
                entitiesByGroup.Remove(groupName);
                OnGroupRemoved(group);
            }
        }
    }
    
    private void OnGroupCreated(EntityGroup group)
    {
        group.GroupChanged += OnGroupChanged;
        GroupCreated?.Invoke(this, new EntityGroupEventArgs(group));
    }

    private void OnGroupRemoved(EntityGroup group)
    {
        group.GroupChanged -= OnGroupChanged;
        GroupRemoved?.Invoke(this, new EntityGroupEventArgs(group));
    }

    private void OnGroupChanged(object sender, GroupChangedEventArgs args)
    {
        GroupChanged?.Invoke(sender, args);
    }

    public void AddToGroup(Entity entity, string groupName, bool createIfNotExists = true)
    {
        if (string.IsNullOrEmpty(groupName))
        {
            throw new ArgumentNullException(nameof(groupName));
        }

        lock (syncObject)
        {
            if (entitiesByGroup.TryGetValue(groupName, out var group))
            {
                group.Add(entity);
            }
            else
            {
                if (!createIfNotExists)
                {
                    throw new Exception($"No entity group available with name {groupName}");
                }

                group = CreateGroup(groupName);
                group.Add(entity);
            }
        }
    }

    public void RemoveFromGroup(Entity entity, string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
        {
            throw new ArgumentNullException(nameof(groupName));
        }

        lock (syncObject)
        {
            if (entitiesByGroup.TryGetValue(groupName, out var group))
            {
                group.Remove(entity);
            }
            else
            {
                throw new Exception($"No entity group available with name {groupName}");
            }
        }
    }

    public void RemoveFromGroup(IEnumerable<Entity> entities, string groupName)
    {
        if (string.IsNullOrEmpty(groupName))
        {
            throw new ArgumentNullException(nameof(groupName));
        }

        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        lock (syncObject)
        {
            if (entitiesByGroup.TryGetValue(groupName, out var group))
            {
                group.Remove(entities);
            }
            else
            {
                throw new Exception($"No entity group available with name {groupName}");
            }
        }
    }

    public EntityGroup GetGroup(string groupName)
    {
        entitiesByGroup.TryGetValue(groupName, out var group);
        return group;
    }

    public void ResetGroup(string groupName)
    {
        lock (syncObject)
        {
            if (entitiesByGroup.TryGetValue(groupName, out var group))
            {
                group.Clear();
            }
        }
    }

    public string[] GetAllGroupIds()
    {
        var groupIds = new string[entitiesByGroup.Count];
        entitiesByGroup.Keys.CopyTo(groupIds, 0);
        return groupIds;
    }

    public EntityGroup[] GetAllGroups()
    {
        var groupIds = new EntityGroup[entitiesByGroup.Count];
        entitiesByGroup.Values.CopyTo(groupIds, 0);
        return groupIds;
    }

    /// <summary>
    /// Search entity with given Uid
    /// </summary>
    /// <param name="uid"><see cref="IIdentifiable.Uid"/></param>
    /// <returns>First found entity</returns>
    public Entity FindEntity(UInt128 uid)
    {
        lock (syncObject)
        {
            availableEntities.TryGetValue(uid, out var entity);
            return entity;
        }
    }

    /// <summary>
    /// Return first entity with given name
    /// </summary>
    /// <param name="name">The entity's name.</param>
    /// <returns>The entity, or null when none has that name.</returns>
    public Entity FindEntity(string name)
    {
        lock (syncObject)
        {
            foreach (var availableEntity in availableEntities.Values)
            {
                if (availableEntity.Name == name)
                {
                    return availableEntity;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Return all entities with given name
    /// </summary>
    /// <param name="name">The entities' name.</param>
    /// <returns>The entities; empty when none has that name.</returns>
    public Entity[] FindEntities(string name)
    {
        lock (syncObject)
        {
            var entities = new List<Entity>();
            foreach (var availableEntity in availableEntities.Values)
            {
                if (availableEntity.Name == name)
                {
                    entities.Add(availableEntity);
                }
            }

            return entities.ToArray();
        }
    }

    public void Reset()
    {
        List<Scene> removed;
        lock (syncObject)
        {
            entitiesReowned.Clear();
            entitiesMoved.Clear();
            _ownersChanged.Clear();
            foreach (var entity in availableEntities.Values)
            {
                entity.SetScene(null);
            }

            // Only the main scene outlives a reset, empty.
            foreach (var scene in scenes)
            {
                scene.Components.Clear();
                scene.Roots.Clear();
                scene.IsRemoved = scene != MainScene;
            }

            removed = scenes.FindAll(scene => scene != MainScene);
            scenes.Clear();
            scenes.Add(MainScene);
            availableEntities.Clear();
            entitiesByGroup.Clear();
            entityParametersMap.Clear();
            availableProjectionTypes.Clear();
        }

        foreach (var scene in removed)
        {
            scene.RaiseRemoved();
        }
    }

    public event EventHandler<EntityEventArgs> EntityAdded;
    public event EventHandler<EntityEventArgs> EntityRemoved;
    public event EventHandler<EntityGroupEventArgs> GroupCreated;
    public event EventHandler<EntityGroupEventArgs> GroupRemoved;
    public event EventHandler<GroupChangedEventArgs> GroupChanged;
}