using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Adamantium.Core.Collections
{
    /// <summary>
    /// Observable collection, which could be used for binding 
    /// </summary>
    /// <remarks>Implements <see cref="INotifyPropertyChanged"/> and <see cref="INotifyPropertyChanged"/> interfaces</remarks>
    /// <typeparam name="T"></typeparam>
    public class AdamantiumCollection<T> : IList<T>, IReadOnlyList<T>, ICollection
    {
        private T[] items;
        private int currentIndex;
        private readonly int defaultCapacity = 5;
        private readonly object syncObject = new object();

        public int Capacity
        {
            get => items.Length;
            set
            {
                if (IsFixedSize)
                    throw new InvalidOperationException("Cannot add items to a fixed-size collection.");
                
                if (value != items.Length)
                {
                    if (value > 0)
                    {
                        var dst = new T[value];
                        if (currentIndex > 0)
                        {
                            Array.Copy(items, 0, dst, 0, currentIndex);
                        }
                        items = dst;
                    }
                    else
                    {
                        items = [];
                    }
                }
            }
        }

        /// <summary>
        /// Constructs <see cref="AdamantiumCollection{T}"/>
        /// </summary>
        public AdamantiumCollection()
        {
            items = new T[defaultCapacity];
        }

        /// <summary>
        /// Constructs <see cref="AdamantiumCollection{T}"/> using capacity
        /// </summary>
        /// <param name="capacity"></param>
        public AdamantiumCollection(Int32 capacity)
        {
            IsFixedSize = true;
            items = new T[capacity];
        }

        /// <summary>
        /// Constructs <see cref="AdamantiumCollection{T}"/> using another IEnumerable collection
        /// </summary>
        /// <param name="items"></param>
        public AdamantiumCollection(IEnumerable<T> items) : this()
        {
            AddRange(items);
        }

        /// <summary>
        /// Constructs <see cref="AdamantiumCollection{T}"/> using params T[] collection (without need to create array)
        /// </summary>
        /// <param name="items"></param>
        public AdamantiumCollection(params T[] items)
        {
            AddRange(items);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>
        /// An enumerator that can be used to iterate through the collection.
        /// </returns>
        /// <filterpriority>1</filterpriority>
        public IEnumerator<T> GetEnumerator()
        {
            lock (SyncRoot)
            {
                return new Enumerator(this);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (SyncRoot)
            {
                return GetEnumerator();
            }
        }

        /// <summary>
        /// Adds an item to the <see cref="AdamantiumCollection{T}"/>.
        /// </summary>
        /// <param name="item">The object to add to the <see cref="AdamantiumCollection{T}"/>.</param>
        public void Add(T item)
        {
            Insert(currentIndex, item);
        }


        /// <summary>
        /// Returns <see cref="ReadOnlyCollection{T}"/> of the <see cref="AdamantiumCollection{T}"/>
        /// </summary>
        /// <returns></returns>
        public IReadOnlyCollection<T> AsReadOnly()
        {
            lock (SyncRoot)
            {
                return new ReadOnlyCollection<T>(this);
            }
        }

        /// <summary>
        /// Removes all items from the <see cref="AdamantiumCollection{T}"/>.
        /// </summary>
        public void Clear()
        {
            lock (SyncRoot)
            {
                if (currentIndex == 0) return;

                // Listeners see the leaving items while they are still here: OnClear runs after the array is zeroed and
                // could not say WHAT left, so per-item unwinding was silently skipped.
                OnClearing(new ArraySegment<T>(items, 0, currentIndex));
                ClearItems();
                OnCleared();
            }
        }

        /// <summary>
        /// Clears items array and set Capacity to 0
        /// </summary>
        protected virtual void ClearItems()
        {
            if (IsFixedSize)
                throw new InvalidOperationException("Cannot add items to a fixed-size collection.");
            
            Array.Clear(items, 0, currentIndex);
            Capacity = 0;
            currentIndex = 0;
        }

        /// <summary>
        /// Inserts the range in the <see cref="AdamantiumCollection{T}"/>
        /// </summary>
        /// <param name="index">The zero-based index at which item should be inserted.</param>
        /// <param name="range"><see cref="IEnumerable{T}"/> of items to insert</param>
        public void InsertRange(int index, IEnumerable<T> range)
        {
            if (IsFixedSize)
                throw new InvalidOperationException("Cannot add items to a fixed-size collection.");
            
            lock (SyncRoot)
            {
                var enumerable = range as T[] ?? range.ToArray();
                for (var i = 0; i < enumerable.Length; i++)
                {
                    var element = enumerable[i];
                    Insert(index + i, element);
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="T:System.Collections.IList"/> has a fixed size.
        /// </summary>
        /// <returns>
        /// true if the <see cref="T:System.Collections.IList"/> has a fixed size; otherwise, false.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public bool IsFixedSize { get; }

        /// <summary>
        /// Gets or sets a validation routine that can be used to validate items before they are
        /// added.
        /// </summary>
        //public Action<T> Validate { get; set; }

        /// <summary>
        /// Determines whether the <see cref="AdamantiumCollection{T}"/> contains a specific value.
        /// </summary>
        /// <returns>
        /// true if <paramref name="item"/> is found in the <see cref="AdamantiumCollection{T}"/>; otherwise, false.
        /// </returns>
        /// <param name="item">The object to locate in the <see cref="AdamantiumCollection{T}"/>.</param>
        public bool Contains(T item)
        {
            lock (SyncRoot)
            {
                for (int i = 0; i < currentIndex; ++i)
                {
                    if (EqualityComparer<T>.Default.Equals(items[i], item))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Copies the elements of the <see cref="T:System.Collections.Generic.ICollection`1"/> to an <see cref="T:System.Array"/>, starting at a particular <see cref="T:System.Array"/> index.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array"/> that is the destination of the elements copied from <see cref="T:System.Collections.Generic.ICollection`1"/>. The <see cref="T:System.Array"/> must have zero-based indexing.</param><param name="arrayIndex">The zero-based index in <paramref name="array"/> at which copying begins.</param><exception cref="T:System.ArgumentNullException"><paramref name="array"/> is null.</exception><exception cref="T:System.ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is less than 0.</exception><exception cref="T:System.ArgumentException">The number of elements in the source <see cref="T:System.Collections.Generic.ICollection`1"/> is greater than the available space from <paramref name="arrayIndex"/> to the end of the destination <paramref name="array"/>.</exception>
        public void CopyTo(T[] array, int arrayIndex)
        {
            lock (SyncRoot)
            {
                if (array == null)
                    throw new ArgumentNullException(nameof(array));

                if (arrayIndex < 0)
                    throw new ArgumentOutOfRangeException(nameof(arrayIndex), "Index must be non-negative.");

                if (array.Length - arrayIndex < Count)
                    throw new ArgumentException("Destination array is not large enough to copy all the items.");

                lock (SyncRoot)
                {
                    Array.Copy(items, 0, array, arrayIndex, Count);
                }
            }
        }

        /// <summary>
        /// Removes the first occurrence of a specific object from the <see cref="AdamantiumCollection{T}"/>.
        /// </summary>
        /// <returns>
        /// true if <paramref name="item"/> was successfully removed from the <see cref="AdamantiumCollection{T}"/>; otherwise, false. This method also returns false if <paramref name="item"/> is not found in the original <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </returns>
        /// <param name="item">The object to remove from the <see cref="AdamantiumCollection{T}"/>.</param>
        public bool Remove(T item)
        {
            lock (SyncRoot)
            {
                // Search only the LIVE range [0, currentIndex): the backing array's trailing slots are stale/default
                // and must not be matched.
                int index = Array.IndexOf(items, item, 0, currentIndex);

                if (index != -1)
                {
                    RemoveInternal(index);
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Removes a set of items defined in <see cref="IEnumerable{T}"/> from <see cref="AdamantiumCollection{T}"/>
        /// </summary>
        /// <param name="elements"><see cref="IEnumerable{T}"/> containing items to remove</param>
        public void Remove(IEnumerable<T> elements)
        {
            lock (SyncRoot)
            {
                foreach (var item in elements)
                {
                    Remove(item);
                }
            }
        }

        /// <summary>
        /// Removes a range of elements from the collection.
        /// </summary>
        /// <param name="index">The first index to remove.</param>
        /// <param name="count">The number of items to remove.</param>
        public virtual void RemoveRange(int index, int count)
        {
            lock (SyncRoot)
            {
                if (index < 0 || index >= currentIndex || count <= 0) return;

                // Remove at the SAME index `count` times: each RemoveInternal shifts the rest down, so `index` is always
                // the next item to drop. (The old `RemoveInternal(i)` for i in [index, index+count) removed every other
                // element and could run off the end as currentIndex shrank.)
                count = Math.Min(count, currentIndex - index);
                for (var i = 0; i < count; i++)
                {
                    RemoveInternal(index);
                }
            }
        }

        /// <summary>
        /// Adds all items from <see cref="IEnumerable{T}"/> to <see cref="AdamantiumCollection{T}"/>
        /// </summary>
        /// <param name="range"><see cref="IEnumerable{T}"/> of items which should be added to the Collection</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void AddRange(IEnumerable<T> range)
        {
            if (range == null)
            {
                return;
            }
            
            if (IsFixedSize)
                throw new InvalidOperationException("Cannot add items to a fixed-size collection.");

            lock (SyncRoot)
            {
                var itemsToAdd = range.ToArray();
                for (var i = 0; i < itemsToAdd.Length; ++i)
                {
                    Insert(currentIndex, itemsToAdd[i]);
                }
            }
        }

        /// <summary>
        /// Gets the number of elements contained in the <see cref="AdamantiumCollection{T}"/>.
        /// </summary>
        /// <returns>
        /// The number of elements contained in the <see cref="AdamantiumCollection{T}"/>.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public int Count => currentIndex;

        /// <summary>The live contents as a span, so a caller that only READS them does not have to copy them out with
        /// ToArray() first. It is a view over the backing array: do not add, remove or clear while holding it.</summary>
        public ReadOnlySpan<T> AsSpan() => new(items, 0, currentIndex);

        /// <summary>
        /// Gets an object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection"/>.
        /// </summary>
        /// <returns>
        /// An object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection"/>.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public object SyncRoot => syncObject;

        /// <summary>The live items as a span over [0, <see cref="Count"/>). MUST be read under a <see cref="SyncRoot"/>
        /// lock (the backing array is replaced on grow) - it exists for the collection's own lock-holding scan helpers,
        /// so they can walk the storage once instead of paying the per-element lock in the indexer.</summary>
        protected System.ReadOnlySpan<T> ItemsSpan => new System.ReadOnlySpan<T>(items, 0, currentIndex);

        /// <summary>
        /// Gets a value indicating whether access to the <see cref="AdamantiumCollection{T}"/> is synchronized (thread safe).
        /// </summary>
        /// <returns>
        /// true if access to the <see cref="AdamantiumCollection{T}"/> is synchronized (thread safe); otherwise, false.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public bool IsSynchronized => true;

        // As ICollection, so a view on another thread finds SyncRoot - the lock every change is made and announced under -
        // and reads the items under it.
        void ICollection.CopyTo(Array array, int index)
        {
            lock (SyncRoot)
            {
                Array.Copy(items, 0, array, index, currentIndex);
            }
        }

        public bool IsReadOnly => false;

        public bool IsEmpty => Count == 0;

        /// <summary>
        /// Determines the index of a specific item in the <see cref="AdamantiumCollection{T}"/>.
        /// </summary>
        /// <returns>
        /// The index of <paramref name="item"/> if found in the list; otherwise, -1.
        /// </returns>
        /// <param name="item">The object to locate in the <see cref="AdamantiumCollection{T}"/>.</param>
        public int IndexOf(T item)
        {
            lock (SyncRoot)
            {
                // Only the live range [0, currentIndex); trailing slots are stale/default.
                return Array.IndexOf(items, item, 0, currentIndex);
            }
        }

        /// <summary>
        /// Inserts an item to the <see cref="AdamantiumCollection{T}"/> at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index at which <paramref name="item"/> should be inserted.</param>
        /// <param name="item">The object to insert into the <see cref="AdamantiumCollection{T}"/>. </param>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="index"/> is not a valid index in the <see cref="T:System.Collections.Generic.IList`1"/>.</exception>
        /// <exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.IList`1"/> is read-only.</exception>
        public void Insert(int index, T item)
        {
            if (IsFixedSize)
                throw new InvalidOperationException("Cannot add items to a fixed-size collection.");
            
            lock (SyncRoot)
            {
                InsertItem(index, item);
                OnInsert(index, item);
            }
        }

        private void CheckCapacity(int size)
        {
            if (Capacity == size)
            {
                var count = (items.Length + 1) * 2;
                if (count < size)
                {
                    count = size;
                }
                Capacity = count;
            }
        }

        /// <summary>
        /// Moves the item at <paramref name="from"/> to <paramref name="to"/>, keeping everything else in order.
        /// <para>ONE change and not a removal followed by an insertion: whoever mirrors this collection - a list of
        /// containers, a set of bindings taken by position - would otherwise see a state in which the item does not
        /// exist at all, and rebuild itself against it.</para>
        /// </summary>
        public void Move(int from, int to)
        {
            if (IsFixedSize)
                throw new InvalidOperationException("Cannot move items in a fixed-size collection.");

            lock (SyncRoot)
            {
                if (from < 0 || from >= currentIndex) throw new ArgumentOutOfRangeException(nameof(from));
                if (to < 0 || to >= currentIndex) throw new ArgumentOutOfRangeException(nameof(to));
                if (from == to) return;

                var item = items[from];

                if (from < to) Array.Copy(items, from + 1, items, from, to - from);
                else Array.Copy(items, to, items, to + 1, from - to);

                items[to] = item;

                OnMove(from, to, item);
            }
        }

        protected virtual void OnMove(int from, int to, T item) { }

        protected virtual void OnInsert(int index, T item) { }

        protected virtual void OnSet(int index, T oldItem, T newItem) { }

        protected virtual void OnRemoveItem(int index, T item) { }
        
        /// <summary>About to be cleared: <paramref name="items"/> is exactly the elements that are leaving, still in place.
        /// It is a window onto the live storage and does not outlive this call - copy anything that must.</summary>
        protected virtual void OnClearing(ArraySegment<T> items) { }

        /// <summary>Cleared. The items are gone; whatever was needed of them was taken in <see cref="OnClearing"/>.</summary>
        protected virtual void OnCleared() { }

        /// <summary>
        /// Removes the item at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the item to remove.</param><exception cref="T:System.ArgumentOutOfRangeException"><paramref name="index"/> is not a valid index in the <see cref="T:System.Collections.Generic.IList`1"/>.</exception><exception cref="T:System.NotSupportedException">The <see cref="T:System.Collections.Generic.IList`1"/> is read-only.</exception>
        public void RemoveAt(int index)
        {
            RemoveInternal(index);
        }

        protected virtual void InsertItem(int index, T item)
        {
            CheckCapacity(currentIndex);

            // Past the end APPENDS: storing at the raw index left every slot between the old end and it empty.
            if (index > currentIndex) index = currentIndex;

            if (index < currentIndex)
            {
                // Shift [index, currentIndex) right by one to open a slot AT index, then store there. (Storing at
                // currentIndex - the end - was the bug: an insert-in-the-middle put the item last and duplicated the
                // element that was at index. Surfaced as a tab dragged to a middle slot "flying to the end".)
                Array.Copy(items, index, items, index + 1, currentIndex - index);
            }
            items[index] = item;
            currentIndex++;
        }

        private void RemoveInternal(int index)
        {
            lock (SyncRoot)
            {
                T item = RemoveItem(index);
                OnRemoveItem(index, item);
            }
        }

        protected virtual T RemoveItem(int index)
        {
            currentIndex--;
            T item = items[index];
            if (index < currentIndex)
            {
                Array.Copy(items, index + 1, items, index, currentIndex - index);
            }
            items[currentIndex] = default(T);

            if (currentIndex == 0)
            {
                Capacity = defaultCapacity;
            }
            else if (items.Length / currentIndex >= 2)
            {
                Capacity = currentIndex;
            }

            return item;
        }

        protected virtual void SetItem(int index, T item)
        {
            items[index] = item;
        }

        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <returns>
        /// The element at the specified index.
        /// </returns>
        /// <param name="index">The zero-based index of the element to get or set.</param><exception cref="T:System.ArgumentOutOfRangeException"><paramref name="index"/> is not a valid index in the <see cref="T:System.Collections.Generic.IList`1"/>.</exception><exception cref="T:System.NotSupportedException">The property is set and the <see cref="T:System.Collections.Generic.IList`1"/> is read-only.</exception>
        public T this[int index]
        {
            get
            {
                lock (SyncRoot)
                {
                    return items[index];
                }
            }
            set
            {
                lock (SyncRoot)
                {
                    T oldItem = items[index];
                    SetItem(index, value);
                    OnSet(index, oldItem, value);
                }
            }
        }

        /// <summary>
        /// Returns an array representation of <see cref="AdamantiumCollection{T}"/>
        /// </summary>
        /// <returns></returns>
        public T[] ToArray()
        {
            lock (SyncRoot)
            {
                T[] array = new T[currentIndex];
                Array.Copy(items, array, currentIndex);
                return array;
            }
        }

        public struct Enumerator : IEnumerator<T>
        {
            private readonly AdamantiumCollection<T> collection;

            private int index;

            private T current;

            internal Enumerator(AdamantiumCollection<T> collection)
            {
                this.collection = collection;
                index = 0;
                current = default(T);
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (index < collection.Count)
                {
                    current = collection.items[index];
                    index++;
                    return true;
                }
                else
                {
                    index = 0;
                    current = default(T);
                    return false;
                }
            }

            public void Reset()
            {
                index = 0;
                current = default(T);
            }

            object IEnumerator.Current => current;

            public T Current => current;
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>
        /// A string that represents the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override string ToString() => "Count : " + Count;

    }


}
