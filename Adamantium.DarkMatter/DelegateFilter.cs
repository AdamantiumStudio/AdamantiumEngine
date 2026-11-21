using System;

namespace Adamantium.DarkMatter;

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
