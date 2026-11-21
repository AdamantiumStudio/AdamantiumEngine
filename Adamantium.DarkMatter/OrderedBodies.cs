using System;
using System.Buffers;

namespace Adamantium.DarkMatter;

/// <summary>
/// A search answered in an order: <c>orderby</c> of the query syntax, <c>ThenBy</c>, or nearest first. The order needs
/// the caller's buffer: <see cref="Into"/> keeps the first bodies in the order, as many as fit, and the rest of the found
/// are passed by. The order is a struct, the sort built for it; nothing is allocated, but by a lambda's closure.
/// </summary>
public readonly ref struct OrderedBodies<TFilter, TOrder>
    where TFilter : IBodyFilter
    where TOrder : IBodyOrder
{
    private const int MostOnTheStack = 128;

    private readonly BodyFinder<TFilter> _found;
    private readonly TOrder _order;
    private readonly int _skip;
    private readonly int _take;

    internal OrderedBodies(BodyFinder<TFilter> found, TOrder order, int skip, int take)
    {
        _found = found;
        _order = order;
        _skip = skip;
        _take = take;
    }

    /// <summary>Where the order finds two alike, by a key of each, the least first.</summary>
    public OrderedBodies<TFilter, ThenOrder<TOrder, KeyOrder<TKey>>> ThenBy<TKey>(Func<BodyView, TKey> key) =>
        Then(new KeyOrder<TKey>(key, false));

    /// <summary>Where the order finds two alike, by a key of each, the greatest first.</summary>
    public OrderedBodies<TFilter, ThenOrder<TOrder, KeyOrder<TKey>>> ThenByDescending<TKey>(Func<BodyView, TKey> key) =>
        Then(new KeyOrder<TKey>(key, true));

    /// <summary>Where the order finds two alike, by an order of the caller's own.</summary>
    public OrderedBodies<TFilter, ThenOrder<TOrder, TNext>> ThenBy<TNext>(TNext order)
        where TNext : IBodyOrder =>
        Then(order);

    /// <summary>Passes by the first <paramref name="count"/> in the order; they are held apart from the caller's buffer,
    /// on the stack when few, in the shared pool when more.</summary>
    public OrderedBodies<TFilter, TOrder> Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new OrderedBodies<TFilter, TOrder>(_found, _order, BodyFinder<TFilter>.AddStretch(_skip, count),
            _take == int.MaxValue ? int.MaxValue : Math.Max(0, _take - count));
    }

    /// <summary>Keeps at most the first <paramref name="count"/> in the order.</summary>
    public OrderedBodies<TFilter, TOrder> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new OrderedBodies<TFilter, TOrder>(_found, _order, _skip, Math.Min(_take, count));
    }

    /// <summary>Groups the bodies found by a key of each, the groups in the order their first body comes in this
    /// order, and this order within each; told before a grouping, <c>Skip</c> and <c>Take</c> are refused.</summary>
    public BodyGroups<TFilter, TOrder, TKey> GroupBy<TKey>(Func<BodyView, TKey> key)
    {
        if (_skip != 0 || _take != int.MaxValue)
            throw new InvalidOperationException("Skip and Take come after a grouping, on its groups.");

        return new BodyGroups<TFilter, TOrder, TKey>(_found, _order, key);
    }

    /// <summary>What each body gives, in the order: <c>select</c> after <c>orderby</c>.</summary>
    public OrderedSelection<TFilter, TOrder, TResult> Select<TResult>(Func<BodyView, TResult> selector) =>
        new(this, selector);

    /// <summary>Writes the bodies found into <paramref name="bodies"/> in the order - the first past those skipped, as
    /// many as fit - and gives how many it wrote.</summary>
    public int Into(Span<BodyId> bodies)
    {
        var room = Math.Min(bodies.Length, _take);
        if (_skip == 0 || room == 0)
            return KeepFirst(bodies[..room]);

        // Room for the skipped as well, no more than there are bodies.
        var needed = (int)Math.Min((long)_skip + room, _found.Simulation.LiveBodyCount);
        BodyId[] rented = null;
        var kept = needed <= MostOnTheStack
            ? stackalloc BodyId[needed]
            : (rented = ArrayPool<BodyId>.Shared.Rent(needed)).AsSpan(0, needed);
        try
        {
            var count = KeepFirst(kept);
            if (count <= _skip)
                return 0;

            kept.Slice(_skip, count - _skip).CopyTo(bodies);
            return count - _skip;
        }
        finally
        {
            if (rented != null)
                ArrayPool<BodyId>.Shared.Return(rented);
        }
    }

    /// <summary>The first body in the order past those skipped; none when nothing is found.</summary>
    public BodyId First()
    {
        Span<BodyId> first = stackalloc BodyId[1];
        return Take(1).Into(first) == 1 ? first[0] : BodyId.None;
    }

    public int Count() => Math.Max(0, Math.Min(_found.Count() - _skip, _take));

    public bool Any() => _take > 0 && _found.Skip(_skip).Any();

    // The first of the found in the order, as many as fit: the buffer filled as they come, then sorted once, and a body
    // coming after kept only if it goes before the last kept.
    internal int KeepFirst(Span<BodyId> bodies)
    {
        if (bodies.IsEmpty)
            return 0;

        var simulation = _found.Simulation;
        var ids = new IdOrder<TOrder>(simulation, _order);
        var count = 0;
        var isSorted = false;
        foreach (var body in _found)
        {
            if (count < bodies.Length)
            {
                bodies[count++] = body.Id;
                continue;
            }

            if (!isSorted)
            {
                bodies.Sort(ids);
                isSorted = true;
            }

            if (_order.Compare(body, simulation.ViewOf(bodies[^1])) >= 0)
                continue;

            var at = bodies.Length - 1;
            while (at > 0 && _order.Compare(simulation.ViewOf(bodies[at - 1]), body) > 0)
            {
                bodies[at] = bodies[at - 1];
                at--;
            }

            bodies[at] = body.Id;
        }

        if (!isSorted)
            bodies[..count].Sort(ids);

        return count;
    }

    private OrderedBodies<TFilter, ThenOrder<TOrder, TNext>> Then<TNext>(TNext next)
        where TNext : IBodyOrder =>
        new(_found, new ThenOrder<TOrder, TNext>(_order, next), _skip, _take);

    internal PhysicsSimulation Simulation => _found.Simulation;
}

/// <summary>What each body of an ordered search gives, in its order. <see cref="Into"/> needs room for the bodies as well
/// as for what they give: a small buffer of them is taken on the stack, a larger one from the shared pool.</summary>
public readonly ref struct OrderedSelection<TFilter, TOrder, TResult>
    where TFilter : IBodyFilter
    where TOrder : IBodyOrder
{
    private const int MostOnTheStack = 128;

    private readonly OrderedBodies<TFilter, TOrder> _ordered;
    private readonly Func<BodyView, TResult> _selector;

    internal OrderedSelection(OrderedBodies<TFilter, TOrder> ordered, Func<BodyView, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _ordered = ordered;
        _selector = selector;
    }

    /// <summary>Writes what the bodies give into <paramref name="results"/> in the order, as many as fit, and gives how
    /// many it wrote.</summary>
    public int Into(Span<TResult> results)
    {
        BodyId[] rented = null;
        var bodies = results.Length <= MostOnTheStack
            ? stackalloc BodyId[results.Length]
            : (rented = ArrayPool<BodyId>.Shared.Rent(results.Length)).AsSpan(0, results.Length);
        try
        {
            var count = _ordered.Into(bodies);
            var simulation = _ordered.Simulation;
            for (var index = 0; index < count; index++)
                results[index] = _selector(simulation.ViewOf(bodies[index]));

            return count;
        }
        finally
        {
            if (rented != null)
                ArrayPool<BodyId>.Shared.Return(rented);
        }
    }
}
