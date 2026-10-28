using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Adamantium.Core;
using Adamantium.Core.Collections;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.MVVM;
using Microsoft.Extensions.ObjectPool;
using IComponent = Adamantium.ECS.Components.IComponent;

namespace Adamantium.ECS
{
    using IComponent = Components.IComponent;

    /// <summary>
    /// Class containing information about entity and its components
    /// </summary>
    [ViewModel]
    public sealed partial class Entity :
       IIdentifiable,
       INamedObject,
       IEnable,
       IControllableComponent,
       IDisposable,
       IEntitySearch
    {
        public Entity(Entity owner = null, String name = "")
        {
            dependencies = new TrackingCollection<Entity>();
            dependenciesReadOnly = new ReadOnlyTrackingCollection<Entity>(dependencies);
            uid = UidGenerator.Generate();
            Owner = owner;
            rootUid = uid;
            if (Owner != null)
            {
                rootUid = GetRoot().rootUid;
            }
            IsEnabled = true;
            Visible = true;
            IsExpanded = true;
            Name = name;
            IconKey = EntityIconKeys.Entity;
            Transform = new Transform();
            componentCollection = new EntityComponentCollection(this);
            componentCollection.Add(Transform);
            readOnlyComponents = new ReadOnlyTrackingCollection<IComponent>(componentCollection);
        }

        private Entity owner;
        private EntityWorld world;
        private readonly UInt128 uid;
        private UInt128 rootUid;
        private readonly TrackingCollection<Entity> dependencies;
        private readonly ReadOnlyTrackingCollection<Entity> dependenciesReadOnly;
        private EntityComponentCollection componentCollection;
        private ReadOnlyTrackingCollection<IComponent> readOnlyComponents;
        private List<IInitializable> pendingComponents = new List<IInitializable>();

        public Transform Transform { get; internal set; }

        public UInt128 Uid => uid;
        
        public UInt128 RootUid => rootUid;

        public bool IgnoreInCollisionDetection { get; set; }

        [Bindable]
        public partial string Name { get; set; }

        /// <summary>The resource key of the icon an editor shows for this entity - one of <see cref="EntityIconKeys"/>,
        /// set by whatever makes an entity of that kind.</summary>
        [Bindable]
        public partial string IconKey { get; set; }

        public bool HasName => !string.IsNullOrEmpty(Name);
        public object Tag { get; set; }
        public bool IsNameImmutable { get; private set; }

        [Bindable]
        public partial bool Visible { get; set; }

        [Bindable]
        public partial bool IsEnabled { get; set; }

        [Bindable]
        public partial bool IsSelected { get; set; }

        /// <summary>The file the entity was made from - a model's path, say; null for one made in the scene. An editor
        /// shows it beside the entity's name rather than in it.</summary>
        [Bindable]
        public partial string Source { get; set; }

        /// <summary>Whether an inspector shows the entity's own section open; open to begin with.</summary>
        [Bindable]
        public partial bool IsExpanded { get; set; }

        /// <summary>The entity this one hangs under; null for a root. Changed through
        /// <see cref="EntityManager.SetOwner"/> and <see cref="EntityManager.RemoveOwner"/>.</summary>
        public Entity Owner
        {
            get => owner;
            internal set
            {
                if (owner == value) return;
                var before = World;
                OnOwnerChanged(owner, value);
                owner = value;
                rootUid = GetRoot().rootUid;
                before?.Changed(UniverseChangedEventArgs.OneFrame);
                if (World != before)
                {
                    NotifyUpdated();
                }
            }
        }

        /// <summary>The world this entity is part of: its root's, once the root is added to one; null outside any.
        /// </summary>
        public EntityWorld World => owner != null ? owner.World : world;

        internal void SetWorld(EntityWorld value) => world = value;

        /// <summary>Tells <see cref="World"/> that what it shows has changed, for one frame.</summary>
        public void NotifyUpdated() => World?.Changed(UniverseChangedEventArgs.OneFrame);

        /// <summary>Tells <see cref="World"/> that what it shows has changed, for the next <paramref name="frames"/>
        /// frames.</summary>
        public void NotifyUpdated(int frames) => World?.Changed(new UniverseChangedEventArgs(frames));

        /// <summary>Tells <see cref="World"/> that what it shows keeps changing for <paramref name="duration"/>.</summary>
        public void NotifyUpdated(TimeSpan duration) => World?.Changed(new UniverseChangedEventArgs(duration));

        protected override void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            base.RaisePropertyChanged(propertyName);
            NotifyUpdated();
        }

        private void OnOwnerChanged(Entity oldOwner, Entity newOwner)
        {
            //Remove this entity from dependeciew of old parent entity
            oldOwner?.RemoveDependency(this);
            //Add current entity as dependency for its parent
            newOwner?.AddDependency(this);

            OwnerChanged?.Invoke(this, new OwnerChangedEventArgs(oldOwner, newOwner));
        }

        public event EventHandler<OwnerChangedEventArgs> OwnerChanged;

        public ReadOnlyTrackingCollection<Entity> Dependencies => dependenciesReadOnly;

        public EntityComponentCollection Components => componentCollection;

        public Entity GetRoot()
        {
            if (Owner == null)
            {
                return this;
            }

            Entity root = Owner;
            bool rootFound = false;
            while (!rootFound)
            {
                if (root.Owner != null)
                {
                    root = root.Owner;
                }
                else
                {
                    rootFound = true;
                }
            }

            return root;
        }

        public Entity Get(string name)
        {
            Queue<Entity> queue = new Queue<Entity>();
            queue.Enqueue(this);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                {
                    return current;
                }

                foreach (var entity in current.Dependencies)
                {
                    queue.Enqueue(entity);
                }
            }
            return null;
        }

        public T GetOrCreateComponent<T>() where T : class, IComponent, new()
        {
            T component = componentCollection.Get<T>();
            if (component == null)
            {
                component = new T();
                AddComponent(component);
            }

            return component;
        }

        public void AddComponent(IComponent component)
        {
            lock (componentCollection)
            {
                if (component is IEntityOwner owned)
                {
                    owned.Owner = this;
                }

                if (ContainsComponent(component.GetType()) || componentCollection.Contains(component))
                    return;

                var customAttribute = component.GetType().GetCustomAttributes(typeof(RequiredComponentAttribute), true);
                foreach (RequiredComponentAttribute attribute in customAttribute)
                {
                    if (!ContainsComponent(attribute.Component))
                    {
                        var required = (IComponent)Activator.CreateInstance(attribute.Component);
                        if (required is IEntityOwner entityOwner)
                        {
                            entityOwner.Owner = this;
                        }

                        if (required is IInitializable init)
                        {
                            pendingComponents.Add(init);
                        }
                        else
                        {
                            componentCollection.Add(required);
                        }
                    }
                }

                foreach (var pendingComponent in pendingComponents)
                {
                    if (pendingComponent is IComponent pending)
                    {
                        componentCollection.Add(pending);
                    }
                    pendingComponent.Initialize();
                }

                componentCollection.Add(component);
                if (component is IInitializable initializable)
                {
                    initializable.Initialize();
                }
                pendingComponents.Clear();
            }
        }

        public void RemoveComponent<T>() where T : class, IComponent
        {
            componentCollection.Remove<T>();
        }

        public void RemoveComponent(IComponent component)
        {
            componentCollection.Remove(component);
        }

        public T GetComponent<T>() where T : class, IComponent
        {
            return componentCollection.Get<T>();
        }

        public T[] GetComponents<T>() where T : class, IComponent
        {
            return componentCollection.GetAll<T>();
        }

        public bool ContainsComponent<T>() where T : class, IComponent
        {
            return componentCollection.Contains<T>();
        }

        public bool ContainsComponent(Type type)
        {
            return componentCollection.Contains(type);
        }

        public T GetComponentInParents<T>() where T : class, IComponent
        {
            return componentCollection.GetInParents<T>();
        }

        public T GetComponentInChildren<T>() where T : class, IComponent
        {
            return componentCollection.GetInChildren<T>();
        }

        public T[] GetComponentsInParents<T>() where T : class, IComponent
        {
            return componentCollection.GetAllInParents<T>();
        }

        public T[] GetComponentsInChildren<T>() where T : class, IComponent
        {
            return componentCollection.GetAllInChildren<T>();
        }

        internal void AddDependency(Entity entity)
        {
            dependencies.Add(entity);
        }

        internal void RemoveDependency(Entity entity)
        {
            dependencies.Remove(entity);
        }

        internal void OnComponentChanged(IComponent oldComponent, IComponent newComponent, ComponentChangedAction action)
        {
            ComponentsChanged?.Invoke(this, new EntityComponentEventArgs(this, oldComponent, newComponent, action));
            NotifyUpdated();
        }

        public event EventHandler<EntityComponentEventArgs> ComponentsChanged;

        // Rent a scratch Stack/Queue from a shared pool instead of allocating one per traversal (that was GC churn). The
        // pool hands each caller its own instance - so reentrant AND concurrent traversals are safe - and the policy clears
        // it on Return, keeping no Entity references alive in the pool.
        private static readonly ObjectPool<Stack<Entity>> StackPool =
            new DefaultObjectPool<Stack<Entity>>(new CollectionPoolPolicy<Stack<Entity>>(s => s.Clear()));
        private static readonly ObjectPool<Queue<Entity>> QueuePool =
            new DefaultObjectPool<Queue<Entity>>(new CollectionPoolPolicy<Queue<Entity>>(q => q.Clear()));

        private sealed class CollectionPoolPolicy<T> : PooledObjectPolicy<T> where T : class, new()
        {
            private readonly Action<T> _clear;
            public CollectionPoolPolicy(Action<T> clear) => _clear = clear;
            public override T Create() => new();
            public override bool Return(T obj) { _clear(obj); return true; }
        }

        public void TraverseInDepth(Action<Entity> action, bool ignoreDisabled = false)
        {
            var stack = StackPool.Get();
            try
            {
                stack.Push(this);
                while (stack.Count > 0)
                {
                    Entity current = stack.Pop();
                    if (ignoreDisabled && !current.IsEnabled) continue;
                    action(current);

                    foreach (var t in current.Dependencies.AsSpan())
                    {
                        if (t != null) stack.Push(t);
                    }
                }
            }
            finally
            {
                StackPool.Return(stack);
            }
        }

        /// <summary>Visits the entity and what is under it, depth first: <paramref name="visit"/> says whether to go under
        /// the entity it is given, so a subtree is skipped by returning false at its root.</summary>
        public void VisitInDepth(Func<Entity, bool> visit)
        {
            var stack = StackPool.Get();
            try
            {
                stack.Push(this);
                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    if (!visit(current))
                    {
                        continue;
                    }

                    foreach (var child in current.Dependencies.AsSpan())
                    {
                        if (child != null) stack.Push(child);
                    }
                }
            }
            finally
            {
                StackPool.Return(stack);
            }
        }

        public void TraverseByLayer(Action<Entity> action, bool ignoreDisabled = false)
        {
            var queue = QueuePool.Get();
            try
            {
                queue.Enqueue(this);
                while (queue.Count > 0)
                {
                    Entity current = queue.Dequeue();
                    if (ignoreDisabled && !current.IsEnabled) continue;
                    action(current);

                    foreach (var child in current.Dependencies.AsSpan())
                    {
                        if (child != null) queue.Enqueue(child);
                    }
                }
            }
            finally
            {
                QueuePool.Return(queue);
            }
        }

        public Entity Duplicate()
        {
            var root = Copy(null, this, $"{Name} (1)");
            var entities = new Dictionary<Entity, Entity>();
            var clones = new List<(ICloneableComponent Original, IComponent Clone)>();
            entities.Add(this, root);
            CloneComponents(root, this, clones);
            TraverseByLayer(current =>
            {
                if (current == this)
                {
                    return;
                }

                if (entities.TryGetValue(current.Owner, out var currentOwner))
                {
                    var entity = Copy(currentOwner, current, current.Name);
                    entities.Add(current, entity);
                    CloneComponents(entity, current, clones);
                }
            });

            foreach (var (original, clone) in clones)
            {
                original.CloneReferences(clone, entities);
            }

            return root;
        }

        private static Entity Copy(Entity owner, Entity original, string name)
        {
            return new Entity(owner, name)
            {
                IconKey = original.IconKey,
                Source = original.Source,
                Visible = original.Visible,
                IsEnabled = original.IsEnabled,
                IsExpanded = original.IsExpanded,
                IgnoreInCollisionDetection = original.IgnoreInCollisionDetection,
                Tag = original.Tag
            };
        }

        private static void CloneComponents(Entity clonedEntity, Entity originalEntity,
            List<(ICloneableComponent Original, IComponent Clone)> clones)
        {
            foreach (var component in originalEntity.Components)
            {
                if (component is Transform transform)
                {
                    transform.CloneValues(clonedEntity.Transform);
                }
                else if (component is ICloneableComponent cloneable)
                {
                    var cloned = cloneable.Clone();
                    cloneable.CloneValues(cloned);
                    clonedEntity.AddComponent(cloned);
                    clones.Add((cloneable, cloned));
                }
            }
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        /// A string that represents the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override string ToString() => $"{Name}: {Uid} ";

        public void Dispose()
        {
            componentCollection.Clear();
        }
    }
}
