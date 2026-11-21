using System;
using System.Buffers;

namespace Adamantium.DarkMatter;

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
