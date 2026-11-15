using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Core.Collections;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Extensions;

namespace Adamantium.ECS
{
    public class EntityComponentCollection : TrackingCollection<IComponent>
    {
        private static readonly object Missing = new();

        private readonly Entity entity;
        private object[] _found = [];

        public EntityComponentCollection(Entity entity)
        {
            this.entity = entity;
        }

        /// <summary>The first component of the type or a derived one, or null. Read without the lock: every processor asks
        /// this of every entity each frame, and the components change rarely. Each type is looked for once, and its answer
        /// kept until the components change.</summary>
        public T Get<T>() where T : class, IComponent
        {
            var found = Volatile.Read(ref _found);
            var slot = ComponentSlot<T>.Index;
            if (slot < found.Length && found[slot] is { } known)
                return known as T;

            var component = Find<T>();
            Remember(found, slot, (object)component ?? Missing);
            return component;
        }

        private T Find<T>() where T : class, IComponent
        {
            var items = AsSpan();
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] is T component)
                    return component;
            }

            return null;
        }

        private void Remember(object[] found, int slot, object answer)
        {
            if (slot < found.Length)
            {
                found[slot] = answer;
                return;
            }

            var grown = new object[Math.Max(slot + 1, found.Length * 2)];
            Array.Copy(found, grown, found.Length);
            grown[slot] = answer;
            Interlocked.CompareExchange(ref _found, grown, found);
        }

        private void ForgetFound() => Volatile.Write(ref _found, new object[Math.Max(1, Volatile.Read(ref _found).Length)]);

        /// <summary>
        /// Gets all the components of the specified type or derived type.
        /// </summary>
        /// <typeparam name="T">Type of the component</typeparam>
        /// <returns>Array of found components</returns>
        public T[] GetAll<T>() where T : class, IComponent
        {
            var items = AsSpan();
            int count = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] is T) count++;
            }

            if (count == 0) return Array.Empty<T>();

            var result = new T[count];
            int next = 0;
            for (int i = 0; i < items.Length && next < count; i++)
            {
                if (items[i] is T component) result[next++] = component;
            }

            return next == count ? result : result[..next];
        }

        public T GetInParents<T>() where T : class, IComponent
        {
            if (entity.Owner != null)
            {
                Entity current = entity.Owner;
                do
                {
                    var component = current.GetComponent<T>();
                    if (component != null)
                    {
                        return component;
                    }
                    current = current.Owner;
                } while (current != null);
            }
            return default(T);
        }

        public T GetInChildren<T>() where T : class, IComponent
        {
            var stack = Entity.StackPool.Get();
            try
            {
                stack.Push(entity);
                while (stack.Count > 0)
                {
                    var current = stack.Pop();

                    if (current != entity)
                    {
                        var component = current.GetComponent<T>();
                        if (component != null)
                        {
                            return component;
                        }
                    }

                    foreach (var child in current.Dependencies.AsSpan())
                    {
                        if (child != null)
                            stack.Push(child);
                    }
                }

                return default(T);
            }
            finally
            {
                Entity.StackPool.Return(stack);
            }
        }

        public T[] GetAllInParents<T>() where T : class, IComponent
        {
            List<T> parentComponents = new List<T>();
            if (entity.Owner != null)
            {
                Entity current = entity.Owner;
                do
                {
                    var components = current.GetComponents<T>();
                    parentComponents.AddRange(components);
                    current = current.Owner;
                }
                while (current != null);
            }
            return parentComponents.ToArray();
        }

        public T[] GetAllInChildren<T>() where T : class, IComponent
        {
            List<T> childrenComponents = [];
            var stack = Entity.StackPool.Get();
            try
            {
                stack.Push(entity);
                while (stack.Count > 0)
                {
                    var current = stack.Pop();

                    if (current != entity)
                    {
                        var components = current.GetComponents<T>();
                        childrenComponents.AddRange(components);
                    }

                    foreach (var child in current.Dependencies.AsSpan())
                    {
                        if (child != null)
                            stack.Push(child);
                    }
                }
            }
            finally
            {
                Entity.StackPool.Return(stack);
            }

            return childrenComponents.ToArray();
        }

        public bool Contains<T>() where T : class, IComponent => Get<T>() != null;

        public bool Contains(Type componentType)
        {
            lock (SyncRoot)
            {
                var items = ItemsSpan;
                for (int i = 0; i < items.Length; i++)
                {
                    var type = items[i].GetType();
                    if (type == componentType || type.InheritsFrom(componentType))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public void Remove<T>() where T : class, IComponent
        {
            for (int i = 0; i < Count; i++)
            {
                if (this[i] is T)
                {
                    RemoveAt(i);
                    break;
                }
            }
        }

        protected override void InsertItem(int index, IComponent item)
        {
            HandleComponent(index, item, false);

            base.InsertItem(index, item);
            ForgetFound();

            entity?.OnComponentChanged(null, item, ComponentChangedAction.Added);
        }

        protected override IComponent RemoveItem(int index)
        {
            var oldComponent = this[index];

            if (oldComponent is Transform)
            {
                entity.Transform = null;
            }

            if (oldComponent is IEntityOwner entityOwner)
            {
                entityOwner.Owner = null;
            }

            base.RemoveItem(index);
            ForgetFound();

            entity?.OnComponentChanged(oldComponent, null, ComponentChangedAction.Removed);

            return oldComponent;
        }

        protected override void SetItem(int index, IComponent item)
        {
            var oldComponent = this[index];

            if (ReferenceEquals(oldComponent, item))
            {
                return;
            }

            HandleComponent(index, item, true);

            base.SetItem(index, item);
            ForgetFound();

            entity?.OnComponentChanged(oldComponent, item, ComponentChangedAction.Replaced);
        }

        protected override void ClearItems()
        {
            for (var i = Count - 1; i >= 0; i--)
            {
                RemoveItem(i);
            }

            base.ClearItems();
            ForgetFound();
        }

        private void HandleComponent<T>(int index, T component, bool isReplacing) where T : IComponent
        {
            if (component == null)
            {
                throw new ArgumentNullException(nameof(component));
            }

            if (Contains(component))
            {
                if (!isReplacing)
                {
                    throw new InvalidOperationException(
                       $"Could not add component of type {typeof(T)} to entity {entity} because component of same type already present in list of components");
                }
            }

            if (component is IEntityOwner entityOwner)
            {
                entityOwner.Owner = entity;
            }
        }
    }
}
