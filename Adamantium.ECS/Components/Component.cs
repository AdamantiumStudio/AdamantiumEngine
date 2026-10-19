using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adamantium.Graphics.Core;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components
{
    // NOT DisposableObject: components are plain data + behavior and own no unmanaged/GPU resources (GPU buffers live in
    // the render-layer geometry cache, not on components). Being IDisposable only invited the dispose pattern -
    // use-after-dispose, ownership ambiguity - into the data layer for no benefit. [ViewModel] supplies the change
    // notification.
    [ViewModel]
    public abstract partial class Component: IComponent, IControllableComponent, IInitializable, IEntityOwner, ICloneableComponent
    {
        protected Component()
        {
            uid = UidGenerator.Generate();
            IsExpanded = true;
        }

        private Entity owner;
        private readonly UInt128 uid;
        private bool initialized;

        public Entity Owner
        {
            get => owner;
            set
            {
                if (owner != value)
                {
                    var old = owner;
                    SetProperty(ref owner, value);
                    OnOwnerChanged(old, owner);
                }
            }
        }

        protected virtual void OnOwnerChanged(Entity oldOwner, Entity newOwner)
        {
            OwnerChanged?.Invoke(this, new OwnerChangedEventArgs(oldOwner, newOwner));
        }

        public event EventHandler<OwnerChangedEventArgs> OwnerChanged;


        public virtual void Initialize()
        {
            initialized = true;
        }

        public UInt128 Uid => uid;
        
        [Bindable]
        public partial string Name { get; set; }

        /// <summary>Whether an inspector shows the component's section open; open to begin with.</summary>
        [Bindable]
        public partial bool IsExpanded { get; set; }

        protected void Traverse(Action<Entity> action)
        {
            var stack = new Stack<Entity>();
            stack.Push(Owner);
            while (stack.Count > 0)
            {
                Entity current = stack.Pop();
                if (current.IsEnabled)
                {
                    action(current);

                    for (int i = 0; i < current.Dependencies.Count; i++)
                    {
                        stack.Push(current.Dependencies[i]);
                    }
                }
            }
        }

        public T GetOrCreateComponent<T>() where T : class, IComponent, new()
        {
            return owner?.GetOrCreateComponent<T>();
        }

        public void AddComponent(IComponent component)
        {
            owner?.AddComponent(component);
        }

        public void RemoveComponent<T>() where T : class, IComponent
        {
            owner?.RemoveComponent<T>();
        }

        public T GetComponent<T>() where T : class, IComponent
        {
            return owner?.GetComponent<T>();
        }

        public T GetComponentInParents<T>() where T : class, IComponent
        {
            return owner?.GetComponentInParents<T>();
        }

        public T GetComponentInChildren<T>() where T : class, IComponent
        {
            return owner?.GetComponentInChildren<T>();
        }

        public T[] GetComponents<T>() where T : class, IComponent
        {
            return owner?.GetComponents<T>();
        }

        public T[] GetComponentsInParents<T>() where T : class, IComponent
        {
            return owner?.GetComponentsInParents<T>();
        }

        public T[] GetComponentsInChildren<T>() where T : class, IComponent
        {
            return owner?.GetComponentsInChildren<T>();
        }

        public bool ContainsComponent<T>() where T : class, IComponent
        {
            if (owner == null)
            {
                return false;
            }
            return owner.ContainsComponent<T>();
        }

        public bool Initialized => initialized;
        public bool Initializing { get; private set; }

        public virtual IComponent Clone()
        {
            return (IComponent)Activator.CreateInstance(GetType());
        }

        public virtual void CloneValues(IComponent clone)
        {
            var originalProperties = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var clonedProperties = clone.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).ToDictionary(x => x.Name);
            foreach (var propertyInfo in originalProperties)
            {
                if (!propertyInfo.CanWrite)
                {
                    continue;
                }

                var attr = propertyInfo.GetCustomAttribute<DoNotCloneAttribute>();
                if (attr != null)
                {
                    continue;
                }

                var property = clonedProperties[propertyInfo.Name];
                var value = propertyInfo.GetValue(this);
                property.SetValue(clone, value);
            }
        }

        public virtual void CloneReferences(IComponent clone, IReadOnlyDictionary<Entity, Entity> copies)
        {
        }

        /// <summary>The copy of <paramref name="entity"/> in <paramref name="copies"/>, or the entity itself when it was not copied.</summary>
        protected static Entity CopyOf(Entity entity, IReadOnlyDictionary<Entity, Entity> copies)
        {
            return entity != null && copies.TryGetValue(entity, out var copy) ? copy : entity;
        }
    }
}
