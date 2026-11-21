using System;

namespace Adamantium.DarkMatter;

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

    public BodyJoinEnumerator<TFilter, TInner, TKey, TResult> GetEnumerator() =>
        new(_found.GetEnumerator(), _inner, _bodyKey, _innerKey, _result);

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
}
