using System;
using System.Collections.Generic;

namespace Adamantium.DarkMatter;

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
