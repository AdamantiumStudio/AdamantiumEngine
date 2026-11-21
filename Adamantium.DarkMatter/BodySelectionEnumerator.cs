using System;

namespace Adamantium.DarkMatter;

/// <summary>Walks what each body of a search gives.</summary>
public ref struct BodySelectionEnumerator<TFilter, TResult>
    where TFilter : IBodyFilter
{
    private readonly Func<BodyView, TResult> _selector;
    private BodyFinderEnumerator<TFilter> _found;

    internal BodySelectionEnumerator(BodyFinderEnumerator<TFilter> found, Func<BodyView, TResult> selector)
    {
        _found = found;
        _selector = selector;
        Current = default;
    }

    public TResult Current { get; private set; }

    public bool MoveNext()
    {
        if (!_found.MoveNext())
            return false;

        Current = _selector(_found.Current);
        return true;
    }
}
