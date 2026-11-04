using System;
﻿using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Adamantium.Core.Collections
{
    public class TrackingCollection<T> : AdamantiumCollection<T>, INotifyPropertyChanged, INotifyCollectionChanged
    {
        public TrackingCollection()
        {

        }

        public TrackingCollection(IEnumerable<T> values) : base(values)
        { }

        /// <summary>
        /// Fires when collection is changed (items added/removed/replaced or collection was cleared)
        /// </summary>
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        /// <summary>
        /// Fires when Count property changes
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the <see cref="CollectionChanged"/> event with an add action.
        /// </summary>
        /// <param name="items">The items that were added.</param>
        /// <param name="index">The starting index.</param>
        protected void NotifyAdd(IList items, int index)
        {
            if (CollectionChanged is { } changed)
            {
                changed(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items, index));
            }

            NotifyCountChanged();
        }

        /// <summary>
        /// Raises the <see cref="CollectionChanged"/> event with an add action.
        /// </summary>
        /// <param name="newItem">The items that were added.</param>
        /// <param name="oldItem">The items that were removed.</param>
        /// <param name="index">The starting index.</param>
        private void NotifyReplace(T oldItem, T newItem, int index)
        {
            if (CollectionChanged is { } changed)
            {
                changed(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newItem, oldItem,
                    index));
            }

            NotifyCountChanged();
        }

        /// <summary>
        /// Raises the <see cref="PropertyChanged"/> event when the <see cref="Count"/> property
        /// changes.
        /// </summary>
        private void NotifyCountChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        /// <summary>
        /// Raises the <see cref="CollectionChanged"/> event with a remove action.
        /// </summary>
        /// <param name="items">The items that were removed.</param>
        /// <param name="index">The starting index.</param>
        protected void NotifyRemove(IList items, int index)
        {
            if (CollectionChanged is { } changed)
            {
                changed(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, items, index));
            }

            NotifyCountChanged();
        }

        /// <summary>
        /// Raises the <see cref="CollectionChanged"/> event with a reset action.
        /// </summary>
        protected void NotifyReset()
        {
            if (CollectionChanged is { } changed)
            {
                changed(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }

            NotifyCountChanged();
        }

        protected void NotifyCollectionChanged(NotifyCollectionChangedAction action, IList items)
        {
            var e = new NotifyCollectionChangedEventArgs(action, items);
            CollectionChanged?.Invoke(this, e);
        }

        protected override void OnInsert(int index, T item)
        {
            if (CollectionChanged is { } changed)
            {
                changed(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add,
                    new List<T>() { item }, index));
            }

            NotifyCountChanged();
        }

        protected override void OnRemoveItem(int index, T item)
        {
            if (CollectionChanged is { } changed)
            {
                changed(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove,
                    new List<T>() { item }, index));
            }

            NotifyCountChanged();
        }

        protected override void OnSet(int index, T oldItem, T newItem)
        {
            NotifyReplace(oldItem, newItem, index);
        }

        // A MOVE and not a remove plus an add: the item never leaves, so the count does not change and nothing
        // downstream has to unwind the item's state and build it again.
        protected override void OnMove(int from, int to, T item)
        {
            CollectionChanged?.Invoke(this,
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, item, to, from));
        }

        private List<T> _clearing;   // the items a Clear() is about to drop - kept only while it is being reported

        // A Clear() is reported as a Remove WITH the items, so a mirror can unwind per-item state; they are copied here,
        // before the wipe, and only when someone listens.
        protected override void OnClearing(ArraySegment<T> items)
        {
            if (CollectionChanged == null || items.Count == 0) return;

            _clearing = new List<T>(items.Count);
            foreach (var item in items) _clearing.Add(item);
        }

        protected override void OnCleared()
        {
            if (_clearing == null) return;

            var removed = _clearing;
            _clearing = null;
            NotifyRemove(removed, 0);
        }
    }
}
