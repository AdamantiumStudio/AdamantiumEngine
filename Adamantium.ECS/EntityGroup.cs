using System;
using System.Collections;
using System.Collections.Generic;
using Adamantium.Core.Collections;

namespace Adamantium.ECS
{
    public class EntityGroup : IEnumerable<Entity>, IEntitySearch
    {
        private readonly AdamantiumCollection<Entity> entities;

        public String Name { get; }

        public EntityGroup(string name)
        {
            Name = name;
            entities = new AdamantiumCollection<Entity>();
        }

        private void OnGroupChanged(GroupChangedEventArgs args)
        {
            GroupChanged?.Invoke(this, args);
        }

        public void Add(Entity entity)
        {
            if (entities.Contains(entity)) return;
            entities.Add(entity);
            OnGroupChanged(new GroupChangedEventArgs(GroupState.Add, null, entity));
        }

        public void Add(IEnumerable<Entity> inEntities)
        {
            var addedEntities = new List<Entity>();
            foreach (var entity in inEntities)
            {
                if (!entities.Contains(entity))
                {
                    addedEntities.Add(entity);
                }
            }

            entities.AddRange(addedEntities);
            OnGroupChanged(new GroupChangedEventArgs(GroupState.Add, null, entities.ToArray()));
        }

        public void Remove(Entity entity)
        {
            if (entities.Remove(entity))
            {
                OnGroupChanged(new GroupChangedEventArgs(GroupState.Remove, new[] { entity }, null));
            }
        }

        public void Remove(IEnumerable<Entity> inEntities)
        {
            List<Entity> removedEntities = new List<Entity>();
            foreach (var entity in inEntities)
            {
                if (entities.Remove(entity))
                {
                    removedEntities.Add(entity);
                }
            }
            OnGroupChanged(new GroupChangedEventArgs(GroupState.Remove, removedEntities.ToArray(), null));
        }

        public bool Contains(Entity entity)
        {
            return entities.Contains(entity);
        }

        private void Set(int index, Entity entity)
        {
            var prevEntity = entities[index];
            if (prevEntity != entity)
            {
                entities[index] = entity;
                OnGroupChanged(new GroupChangedEventArgs(GroupState.Replace, new[] { prevEntity }, entity));
            }
        }

        public int Count => entities.Count;

        public Entity this[int index]
        {
            get => entities[index];
            set => Set(index, value);
        }

        public void Clear()
        {
            if (entities.Count <= 0) return;
            var entitiesArray = entities.ToArray();
            entities.Clear();
            OnGroupChanged(new GroupChangedEventArgs(GroupState.Reset, entitiesArray));
        }

        public Entity Get(string name)
        {
            foreach (var entity in entities)
            {
                var queue = new Queue<Entity>();
                queue.Enqueue(entity);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (current.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return current;
                    }

                    foreach (var dependency in current.Dependencies)
                    {
                        queue.Enqueue(dependency);
                    }
                }
            }
            return null;
        }

        public event EventHandler<GroupChangedEventArgs> GroupChanged;

        public IEnumerator<Entity> GetEnumerator()
        {
            return entities.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
