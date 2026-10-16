using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.Collections;
using Adamantium.ECS.Components;
using Adamantium.ECS.Templates;

namespace Adamantium.ECS;

public sealed class EntityManager
{
    private object syncObject = new object();

    private readonly TrackingCollection<Entity> rootEntities;
    private readonly Dictionary<UInt128, Entity> availableEntities;
    private readonly Dictionary<String, EntityGroup> entitiesByGroup;
    private readonly List<Entity> entitiesToAdd;
    private readonly List<Entity> entitiesToRemove;
    private readonly List<Entity> entitiesReowned;
    private readonly Dictionary<UInt128, EntityParameters> entityParametersMap;
    private readonly List<CameraProjectionType> availableProjectionTypes;

    internal EntityManager(EntityWorld world)
    {
        rootEntities = new TrackingCollection<Entity>();
        RootEntities = new ReadOnlyTrackingCollection<Entity>(rootEntities);
        availableEntities = new Dictionary<UInt128, Entity>();
        entitiesByGroup = new Dictionary<String, EntityGroup>();
        entitiesToAdd = new List<Entity>();
        entitiesToRemove = new List<Entity>();
        entitiesReowned = new List<Entity>();
        entityParametersMap = new Dictionary<UInt128, EntityParameters>();
        availableProjectionTypes = new List<CameraProjectionType>();

        World = world;
    }
    
    internal void InitializeResources()
    {
    }

    public EntityWorld World { get; }

    /// <summary>The roots of the world's tree: its entities with no owner, raising a change as they are added and
    /// removed, so a view can bind to them. One given an owner - a camera following a model - becomes a branch of its
    /// owner's tree and leaves them, and comes back once it has none. The changes come on the world's thread.</summary>
    public ReadOnlyTrackingCollection<Entity> RootEntities { get; }

    internal void SyncEntities()
    {
        lock (syncObject)
        {
            if (entitiesToRemove.Count > 0)
            {
                foreach (var entity in entitiesToRemove)
                {
                    RemoveEntityInternal(entity);
                }

                entitiesToRemove.Clear();
            }

            if (entitiesToAdd.Count > 0)
            {
                foreach (var entity in entitiesToAdd)
                {
                    AddEntityInternal(entity);
                }

                entitiesToAdd.Clear();
            }

            if (entitiesReowned.Count > 0)
            {
                foreach (var entity in entitiesReowned)
                {
                    PlaceByOwner(entity);
                }

                entitiesReowned.Clear();
            }
        }
    }

    private void AddEntityInternal(Entity entity)
    {
        if (availableEntities.TryAdd(entity.Uid, entity))
        {
            PlaceByOwner(entity);
            OnEntityAdded(entity);
        }
    }

    /// <summary>Hangs <paramref name="entity"/> under <paramref name="owner"/> - with <see cref="RemoveOwner"/>, the one
    /// way an entity changes owner. A root given an owner leaves <see cref="RootEntities"/>: from the next frame, on the
    /// world's thread.</summary>
    public void SetOwner(Entity entity, Entity owner)
    {
        if (entity == null || entity.Owner == owner) return;

        lock (syncObject)
        {
            entity.Owner = owner;
            entitiesReowned.Add(entity);
        }
    }

    /// <summary>Takes <paramref name="entity"/> off its owner; one of the world's comes back among
    /// <see cref="RootEntities"/> from the next frame.</summary>
    public void RemoveOwner(Entity entity) => SetOwner(entity, null);

    // A root while it has no owner; with one it is a branch of its owner's tree.
    private void PlaceByOwner(Entity entity)
    {
        if (!availableEntities.ContainsKey(entity.Uid)) return;

        var held = entity.Owner != null;
        var isRoot = rootEntities.Contains(entity);
        if (!held && !isRoot)
        {
            rootEntities.Add(entity);
        }
        else if (held && isRoot)
        {
            rootEntities.Remove(entity);
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

        rootEntities.Remove(entity);
        OnEntityRemoved(entity);

        // Its tree goes with it: an entity of the world it holds - a camera following it - leaves the world too.
        entity.TraverseInDepth(held =>
        {
            if (held != entity) RemoveEntityInternal(held);
        });
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

    public void AddEntity(Entity root, CameraProjectionType projectionType = CameraProjectionType.Perspective)
    {
        if (root == null)
        {
            return;
        }

        lock (syncObject)
        {
            if (availableEntities.ContainsKey(root.Uid)) return;

            SetCameraProjectionType(root, projectionType);
            entitiesToAdd.Add(root);
        }
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

    public void RemoveEntity(Entity root)
    {
        if (root == null)
            return;

        lock (syncObject)
        {
            if (!availableEntities.ContainsKey(root.Uid)) return;

            entityParametersMap.Remove(root.RootUid);
            root.Owner?.RemoveDependency(root);
            entitiesToRemove.Add(root);
        }
    }

    /// <summary>Takes every root out of the world, and with each its tree.</summary>
    public void RemoveAllEntities()
    {
        lock (syncObject)
        {
            foreach (var root in rootEntities)
            {
                RemoveEntity(root);
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
    /// <param name="name"><see cref="IName.Name"/></param>
    /// <returns></returns>
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
    /// <param name="name"><see cref="IName.Name"/></param>
    /// <returns></returns>
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
        lock (syncObject)
        {
            entitiesReowned.Clear();
            rootEntities.Clear();
            availableEntities.Clear();
            entitiesByGroup.Clear();
            entityParametersMap.Clear();
            availableProjectionTypes.Clear();
        }
    }
    
    public event EventHandler<EntityEventArgs> EntityAdded;
    public event EventHandler<EntityEventArgs> EntityRemoved;
    public event EventHandler<EntityGroupEventArgs> GroupCreated;
    public event EventHandler<EntityGroupEventArgs> GroupRemoved;
    public event EventHandler<GroupChangedEventArgs> GroupChanged;
}