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
