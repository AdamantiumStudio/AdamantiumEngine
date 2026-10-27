using System;
using System.Collections;
using System.Collections.Generic;
using Adamantium.Core;

namespace Adamantium.Graphics.Core.EffectsFramework;

/// <summary>What an effect holds of one kind - its techniques, a technique's passes, its parameters or its constant
/// buffers - in the order it declares them, each found by its index or by its name.</summary>
public sealed class EffectCollection<T> : IReadOnlyList<T> where T : class, INamedObject
{
    private readonly List<T> items = [];
    private readonly Dictionary<string, T> byName = [];

    internal EffectCollection()
    {
    }

    /// <summary>The one at <paramref name="index"/>, or null past the end.</summary>
    public T this[int index] => (uint)index < (uint)items.Count ? items[index] : null;

    /// <summary>The one named <paramref name="name"/>, or null when there is none.</summary>
    public T this[string name] => byName.GetValueOrDefault(name);

    public int Count => items.Count;

    /// <summary>Whether one is named <paramref name="name"/>.</summary>
    public bool Contains(string name) => byName.ContainsKey(name);

    public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal T Add(T item)
    {
        byName.Add(item.Name, item);
        items.Add(item);
        return item;
    }

    internal void Clear()
    {
        items.Clear();
        byName.Clear();
    }

    internal void Sort(Comparison<T> comparison) => items.Sort(comparison);
}
