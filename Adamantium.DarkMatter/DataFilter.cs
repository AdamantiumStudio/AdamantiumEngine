using System;

namespace Adamantium.DarkMatter;

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
