using System;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Typed;

/// <summary>Checks of the source and destination spans the batch operations take.</summary>
internal static class SpanArguments
{
    /// <summary>Throws when <paramref name="destination"/> is shorter than <paramref name="source"/>, or the part of it
    /// that is written shares memory with the source other than as the very same elements - an operation in
    /// place.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination"/> is too short.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> partly overlaps <paramref name="source"/>.</exception>
    public static void CheckDestination<TSource, TDestination>(ReadOnlySpan<TSource> source, Span<TDestination> destination)
        where TSource : unmanaged where TDestination : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(destination.Length, source.Length, nameof(destination));
        var from = MemoryMarshal.AsBytes(source);
        var to = MemoryMarshal.AsBytes((ReadOnlySpan<TDestination>)destination[..source.Length]);
        if (from.Overlaps(to, out var offset) && (offset != 0 || typeof(TSource) != typeof(TDestination)))
        {
            throw new ArgumentException("The destination overlaps the source other than in place.", nameof(destination));
        }
    }
}
