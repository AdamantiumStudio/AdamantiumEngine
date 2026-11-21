using System;

namespace Adamantium.DarkMatter;

/// <summary>A condition a search of <see cref="PhysicsSimulation.Find"/> keeps bodies by. A struct of the caller's
/// own is called straight, with no delegate: the JIT builds the search for it and inlines it.</summary>
public interface IBodyFilter
{
    bool Matches(in BodyView body);
}

/// <summary>Every body: the search before any <c>Where</c>.</summary>
public readonly struct AnyBody : IBodyFilter
{
    public bool Matches(in BodyView body) => true;
}

/// <summary>Bodies that both conditions keep: one <c>Where</c> after another.</summary>
public readonly struct BothFilters<TFirst, TSecond> : IBodyFilter
    where TFirst : IBodyFilter
    where TSecond : IBodyFilter
{
    private readonly TFirst _first;
    private readonly TSecond _second;

    internal BothFilters(TFirst first, TSecond second)
    {
        _first = first;
        _second = second;
    }

    public bool Matches(in BodyView body) => _first.Matches(body) && _second.Matches(body);
}

/// <summary>A predicate with the data it looks at, given apart: a static lambda takes no closure, so asks nothing of
/// the memory.</summary>
public readonly struct DataFilter<TData> : IBodyFilter
{
    private readonly TData _data;
    private readonly Func<BodyView, TData, bool> _predicate;

    internal DataFilter(TData data, Func<BodyView, TData, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _data = data;
        _predicate = predicate;
    }

    public bool Matches(in BodyView body) => _predicate(body, _data);
}

/// <summary>A predicate of any kind, as <c>where</c> of the query syntax gives it; one that takes in what is around it
/// makes a closure, which the caller pays for.</summary>
public readonly struct DelegateFilter : IBodyFilter
{
    private readonly Func<BodyView, bool> _predicate;

    internal DelegateFilter(Func<BodyView, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _predicate = predicate;
    }

    public bool Matches(in BodyView body) => _predicate(body);
}
