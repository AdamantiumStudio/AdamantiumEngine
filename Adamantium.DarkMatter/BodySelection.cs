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

    public BodySelectionEnumerator<TFilter, TResult> GetEnumerator() => new(_found.GetEnumerator(), _selector);

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
}
