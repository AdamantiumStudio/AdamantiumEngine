using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter;

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
}
