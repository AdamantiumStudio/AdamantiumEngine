using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter;

/// <summary>What each body of a search gives: <c>select</c> of the query syntax. Walked by <c>foreach</c>, or written
/// into the caller's buffer.</summary>
public readonly ref struct BodySelection<TFilter, TResult>
    where TFilter : IBodyFilter
{
    private readonly BodyFinder<TFilter> _found;
    private readonly Func<BodyView, TResult> _selector;

    internal BodySelection(BodyFinder<TFilter> found, Func<BodyView, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        _found = found;
        _selector = selector;
    }

    /// <summary>Gives each value once, as <see cref="EqualityComparer{T}.Default"/> tells them apart.</summary>
    public DistinctSelection<TFilter, TResult> Distinct() => new(this);

    public Enumerator GetEnumerator() => new(_found.GetEnumerator(), _selector);

    /// <summary>Writes what the bodies give into <paramref name="results"/>, as many as fit, and gives how many it
    /// wrote.</summary>
    public int Into(Span<TResult> results)
    {
        var written = 0;
        foreach (var result in this)
        {
            if (written == results.Length)
                break;

            results[written++] = result;
        }

        return written;
    }

    public int Count() => _found.Count();

    public ref struct Enumerator
    {
        private BodyFinder<TFilter>.Enumerator _found;
        private readonly Func<BodyView, TResult> _selector;

        internal Enumerator(BodyFinder<TFilter>.Enumerator found, Func<BodyView, TResult> selector)
        {
            _found = found;
            _selector = selector;
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
}

/// <summary>What the bodies of a search give, each value once: written into the caller's buffer, which it looks through
/// for each value, so it suits a buffer of few.</summary>
public readonly ref struct DistinctSelection<TFilter, TResult>
    where TFilter : IBodyFilter
{
    private readonly BodySelection<TFilter, TResult> _selection;

    internal DistinctSelection(BodySelection<TFilter, TResult> selection) => _selection = selection;

    /// <summary>Writes each value once into <paramref name="results"/>, as many as fit, and gives how many it wrote.
    /// </summary>
    public int Into(Span<TResult> results)
    {
        var written = 0;
        foreach (var result in _selection)
        {
            if (written == results.Length)
                break;

            if (results[..written].IndexOf(result, EqualityComparer<TResult>.Default) < 0)
                results[written++] = result;
        }

        return written;
    }
}

/// <summary>Each body of a search met with each item of a span whose key is its own: <c>join … on … equals …</c> of the
/// query syntax. Walks the span once for each body found.</summary>
public readonly ref struct BodyJoin<TFilter, TInner, TKey, TResult>
    where TFilter : IBodyFilter
{
    private readonly BodyFinder<TFilter> _found;
    private readonly ReadOnlySpan<TInner> _inner;
    private readonly Func<BodyView, TKey> _bodyKey;
    private readonly Func<TInner, TKey> _innerKey;
    private readonly Func<BodyView, TInner, TResult> _result;

    internal BodyJoin(BodyFinder<TFilter> found, ReadOnlySpan<TInner> inner, Func<BodyView, TKey> bodyKey,
        Func<TInner, TKey> innerKey, Func<BodyView, TInner, TResult> result)
    {
        ArgumentNullException.ThrowIfNull(bodyKey);
        ArgumentNullException.ThrowIfNull(innerKey);
        ArgumentNullException.ThrowIfNull(result);
        _found = found;
        _inner = inner;
        _bodyKey = bodyKey;
        _innerKey = innerKey;
        _result = result;
    }

    public Enumerator GetEnumerator() => new(_found.GetEnumerator(), _inner, _bodyKey, _innerKey, _result);

    /// <summary>Writes what each pair met gives into <paramref name="results"/>, as many as fit, and gives how many it
    /// wrote.</summary>
    public int Into(Span<TResult> results)
    {
        var written = 0;
        foreach (var result in this)
        {
            if (written == results.Length)
                break;

            results[written++] = result;
        }

        return written;
    }

    public int Count()
    {
        var count = 0;
        foreach (var _ in this)
            count++;

        return count;
    }

    public ref struct Enumerator
    {
        private BodyFinder<TFilter>.Enumerator _found;
        private readonly ReadOnlySpan<TInner> _inner;
        private readonly Func<BodyView, TKey> _bodyKey;
        private readonly Func<TInner, TKey> _innerKey;
        private readonly Func<BodyView, TInner, TResult> _result;
        private TKey _key;
        private int _at;
        private bool _hasBody;

        internal Enumerator(BodyFinder<TFilter>.Enumerator found, ReadOnlySpan<TInner> inner,
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
}
