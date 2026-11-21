using System;
using System.Buffers;
using System.Collections.Generic;

namespace Adamantium.DarkMatter;

/// <summary>
/// A search grouped by a key of each body: <c>group … by</c> of the query syntax, as LINQ groups. <see cref="Into"/>
/// writes the bodies found into the caller's buffer group after group - the groups in the order their first body comes,
/// the bodies of each in the order told - and the groups are slices of it. Keys are told apart as
/// <see cref="EqualityComparer{T}.Default"/> does; found past the buffer's room, the last in the order are passed by.
/// </summary>
public readonly ref struct BodyGroups<TFilter, TOrder, TKey>
    where TFilter : IBodyFilter
    where TOrder : IBodyOrder
{
    private readonly BodyFinder<TFilter> _found;
    private readonly TOrder _order;
    private readonly Func<BodyView, TKey> _key;

    internal BodyGroups(BodyFinder<TFilter> found, TOrder order, Func<BodyView, TKey> key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _found = found;
        _order = order;
        _key = key;
    }

    /// <summary>Writes the bodies found into <paramref name="buffer"/> group after group, and gives the groups, slices
    /// of it. A pass over the bodies for each group: it suits groups of a few keys.</summary>
    public BodyGrouping<TKey> Into(Span<BodyId> buffer)
    {
        var simulation = _found.Simulation;
        var count = new OrderedBodies<TFilter, TOrder>(_found, _order, 0, int.MaxValue).KeepFirst(buffer);
        var bodies = buffer[..count];
        var grouped = ArrayPool<BodyId>.Shared.Rent(Math.Max(1, count));
        var isTaken = ArrayPool<bool>.Shared.Rent(Math.Max(1, count));
        try
        {
            Array.Clear(isTaken, 0, count);
            var written = 0;
            for (var first = 0; first < count; first++)
            {
                if (isTaken[first])
                    continue;

                var key = _key(simulation.ViewOf(bodies[first]));
                for (var next = first; next < count; next++)
                {
                    if (isTaken[next]
                        || next != first && !EqualityComparer<TKey>.Default.Equals(key, _key(simulation.ViewOf(bodies[next]))))
                        continue;

                    isTaken[next] = true;
                    grouped[written++] = bodies[next];
                }
            }

            grouped.AsSpan(0, count).CopyTo(bodies);
        }
        finally
        {
            ArrayPool<BodyId>.Shared.Return(grouped);
            ArrayPool<bool>.Shared.Return(isTaken);
        }

        return new BodyGrouping<TKey>(simulation, bodies, _key);
    }
}

/// <summary>The groups of a grouped search: runs of one key in the caller's buffer, each a slice of it. Good until the
/// buffer is written again or the simulation reads a step: a body walked since taken out is refused.</summary>
public readonly ref struct BodyGrouping<TKey>
{
    private readonly PhysicsSimulation _simulation;
    private readonly ReadOnlySpan<BodyId> _bodies;
    private readonly Func<BodyView, TKey> _key;

    internal BodyGrouping(PhysicsSimulation simulation, ReadOnlySpan<BodyId> bodies, Func<BodyView, TKey> key)
    {
        _simulation = simulation;
        _bodies = bodies;
        _key = key;
    }

    /// <summary>All the bodies grouped, group after group.</summary>
    public ReadOnlySpan<BodyId> Bodies => _bodies;

    /// <summary>How many groups there are.</summary>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var _ in this)
                count++;

            return count;
        }
    }

    public Enumerator GetEnumerator() => new(_simulation, _bodies, _key);

    public ref struct Enumerator
    {
        private readonly PhysicsSimulation _simulation;
        private readonly ReadOnlySpan<BodyId> _bodies;
        private readonly Func<BodyView, TKey> _key;
        private int _next;

        internal Enumerator(PhysicsSimulation simulation, ReadOnlySpan<BodyId> bodies, Func<BodyView, TKey> key)
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
}

/// <summary>One group of a grouped search: its key, and its bodies, a slice of the caller's buffer.</summary>
public readonly ref struct BodyGroup<TKey>
{
    internal BodyGroup(TKey key, ReadOnlySpan<BodyId> bodies)
    {
        Key = key;
        Bodies = bodies;
    }

    public TKey Key { get; }
    public ReadOnlySpan<BodyId> Bodies { get; }
    public int Count => Bodies.Length;
}
