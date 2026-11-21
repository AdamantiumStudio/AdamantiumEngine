using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter;

/// <summary>Walks the pairs of a join: each body found, with each item of the span whose key is its own.</summary>
public ref struct BodyJoinEnumerator<TFilter, TInner, TKey, TResult>
    where TFilter : IBodyFilter
{
    private readonly ReadOnlySpan<TInner> _inner;
    private readonly Func<BodyView, TKey> _bodyKey;
    private readonly Func<TInner, TKey> _innerKey;
    private readonly Func<BodyView, TInner, TResult> _result;
    private BodyFinderEnumerator<TFilter> _found;
    private TKey _key;
    private int _at;
    private bool _hasBody;

    internal BodyJoinEnumerator(BodyFinderEnumerator<TFilter> found, ReadOnlySpan<TInner> inner,
        Func<BodyView, TKey> bodyKey, Func<TInner, TKey> innerKey, Func<BodyView, TInner, TResult> result)
    {
        _found = found;
        _inner = inner;
        _bodyKey = bodyKey;
        _innerKey = innerKey;
        _result = result;
        _key = default;
        _at = 0;
        _hasBody = false;
        Current = default;
    }

    public TResult Current { get; private set; }

    public bool MoveNext()
    {
        while (true)
        {
            if (!_hasBody)
            {
                if (!_found.MoveNext())
                    return false;

                _hasBody = true;
                _key = _bodyKey(_found.Current);
                _at = 0;
            }

            while (_at < _inner.Length)
            {
                ref readonly var item = ref _inner[_at++];
                if (!EqualityComparer<TKey>.Default.Equals(_key, _innerKey(item)))
                    continue;

                Current = _result(_found.Current, item);
                return true;
            }

            _hasBody = false;
        }
    }
}
