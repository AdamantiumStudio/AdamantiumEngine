using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter;

/// <summary>Walks the groups of a grouped search, each a run of one key in the caller's buffer.</summary>
public ref struct BodyGroupingEnumerator<TKey>
{
    private readonly PhysicsSimulation _simulation;
    private readonly ReadOnlySpan<BodyId> _bodies;
    private readonly Func<BodyView, TKey> _key;
    private int _next;

    internal BodyGroupingEnumerator(PhysicsSimulation simulation, ReadOnlySpan<BodyId> bodies, Func<BodyView, TKey> key)
    {
        _simulation = simulation;
        _bodies = bodies;
        _key = key;
        _next = 0;
        Current = default;
    }

    public BodyGroup<TKey> Current { get; private set; }

    public bool MoveNext()
    {
        if (_next >= _bodies.Length)
            return false;

        var start = _next;
        var key = _key(_simulation.ViewOfKept(_bodies[start]));
        _next++;
        while (_next < _bodies.Length
               && EqualityComparer<TKey>.Default.Equals(key, _key(_simulation.ViewOfKept(_bodies[_next]))))
            _next++;

        Current = new BodyGroup<TKey>(key, _bodies[start.._next]);
        return true;
    }
}
