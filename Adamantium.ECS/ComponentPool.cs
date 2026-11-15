using System;
using System.Collections.Generic;
using Adamantium.ECS.Components;

namespace Adamantium.ECS;

// The components of one type in a scene, packed in an array beside the entities that hold them. Adding and taking out
// cost the same however many there are; taken out while the scene is walked, a component leaves a gap that is closed
// once the last walk ends, so a walk never sees the array move under it.
internal sealed class ComponentPool
{
    private readonly Dictionary<IComponent, int> _positions = new(ReferenceEqualityComparer.Instance);

    public IComponent[] Items { get; private set; } = [];

    public Entity[] Owners { get; private set; } = [];

    public int Count { get; private set; }

    public bool HasGaps { get; private set; }

    public void Add(IComponent component, Entity owner)
    {
        if (_positions.ContainsKey(component))
            return;

        if (Count == Items.Length)
            Grow();

        Items[Count] = component;
        Owners[Count] = owner;
        _positions[component] = Count;
        Count++;
    }

    // True when it was here.
    public bool Remove(IComponent component, bool leaveGap)
    {
        if (!_positions.Remove(component, out var at))
            return false;

        if (leaveGap)
        {
            Items[at] = null;
            Owners[at] = null;
            HasGaps = true;
            return true;
        }

        var last = Count - 1;
        if (at != last)
        {
            Items[at] = Items[last];
            Owners[at] = Owners[last];
            _positions[Items[at]] = at;
        }

        Items[last] = null;
        Owners[last] = null;
        Count = last;
        return true;
    }

    // Closes the gaps, keeping the order of what is left.
    public void CloseGaps()
    {
        var kept = 0;
        for (var read = 0; read < Count; read++)
        {
            var component = Items[read];
            if (component == null)
                continue;

            if (kept != read)
            {
                Items[kept] = component;
                Owners[kept] = Owners[read];
                _positions[component] = kept;
            }

            kept++;
        }

        Array.Clear(Items, kept, Count - kept);
        Array.Clear(Owners, kept, Count - kept);
        Count = kept;
        HasGaps = false;
    }

    private void Grow()
    {
        var size = Math.Max(4, Items.Length * 2);
        var items = Items;
        var owners = Owners;
        Array.Resize(ref items, size);
        Array.Resize(ref owners, size);
        Items = items;
        Owners = owners;
    }
}
