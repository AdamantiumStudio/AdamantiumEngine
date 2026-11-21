using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter;

/// <summary>An order a search of <see cref="PhysicsSimulation.Find"/> answers in: which of two bodies comes first. A
/// struct, so the sort is built for it and calls it straight.</summary>
public interface IBodyOrder
{
    /// <summary>Below zero when <paramref name="first"/> comes before <paramref name="second"/>, above when after,
    /// zero when either.</summary>
    int Compare(in BodyView first, in BodyView second);
}

/// <summary>By a key of each body, as <c>orderby</c> of the query syntax gives it; keys are compared as
/// <see cref="Comparer{T}.Default"/> does.</summary>
public readonly struct KeyOrder<TKey> : IBodyOrder
{
    private readonly Func<BodyView, TKey> _key;
    private readonly bool _isDescending;

    internal KeyOrder(Func<BodyView, TKey> key, bool isDescending)
    {
        ArgumentNullException.ThrowIfNull(key);
        _key = key;
        _isDescending = isDescending;
    }

    public int Compare(in BodyView first, in BodyView second)
    {
        var order = Comparer<TKey>.Default.Compare(_key(first), _key(second));
        return _isDescending ? -order : order;
    }

    internal TKey KeyOf(in BodyView body) => _key(body);
}

/// <summary>By one order, and by another where the first finds two alike: <c>ThenBy</c>.</summary>
public readonly struct ThenOrder<TFirst, TSecond> : IBodyOrder
    where TFirst : IBodyOrder
    where TSecond : IBodyOrder
{
    private readonly TFirst _first;
    private readonly TSecond _second;

    internal ThenOrder(TFirst first, TSecond second)
    {
        _first = first;
        _second = second;
    }

    public int Compare(in BodyView first, in BodyView second)
    {
        var order = _first.Compare(first, second);
        return order != 0 ? order : _second.Compare(first, second);
    }
}

/// <summary>Nearest to a point first, by the center of mass.</summary>
public readonly struct NearestOrder : IBodyOrder
{
    private readonly WorldPosition _point;

    internal NearestOrder(WorldPosition point) => _point = point;

    public int Compare(in BodyView first, in BodyView second) =>
        (first.CenterOfMass - _point).LengthSquared().CompareTo((second.CenterOfMass - _point).LengthSquared());
}

/// <summary>No order: the bodies alike, as a grouping with no order told keeps them.</summary>
public readonly struct NoOrder : IBodyOrder
{
    public int Compare(in BodyView first, in BodyView second) => 0;
}

// The order told over the ids in a caller's buffer, for the span's own sort: each id read as the last step left it.
internal readonly struct IdOrder<TOrder>(PhysicsSimulation simulation, TOrder order) : IComparer<BodyId>
    where TOrder : IBodyOrder
{
    public int Compare(BodyId first, BodyId second) =>
        order.Compare(simulation.ViewOf(first), simulation.ViewOf(second));
}
