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

    /// <summary>Throws when the three sources differ in length, a destination is shorter, a destination shares memory
    /// with a source other than as the very same elements, or two destinations share memory: points kept as separate
    /// arrays of X, Y and Z, each element read before its own is written.</summary>
    /// <exception cref="ArgumentException">The sources differ in length, or the spans overlap other than in place.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A destination is too short.</exception>
    public static void CheckComponents<TSource, TDestination>(ReadOnlySpan<TSource> x, ReadOnlySpan<TSource> y, ReadOnlySpan<TSource> z,
        Span<TDestination> destinationX, Span<TDestination> destinationY, Span<TDestination> destinationZ)
        where TSource : unmanaged where TDestination : unmanaged
    {
        if (y.Length != x.Length || z.Length != x.Length)
        {
            throw new ArgumentException("The X, Y and Z of the points differ in count.", nameof(y));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(destinationX.Length, x.Length, nameof(destinationX));
        ArgumentOutOfRangeException.ThrowIfLessThan(destinationY.Length, x.Length, nameof(destinationY));
        ArgumentOutOfRangeException.ThrowIfLessThan(destinationZ.Length, x.Length, nameof(destinationZ));
        ReadOnlySpan<TDestination> written0 = destinationX[..x.Length], written1 = destinationY[..x.Length], written2 = destinationZ[..x.Length];
        if (Overlap(written0, written1) || Overlap(written0, written2) || Overlap(written1, written2))
        {
            throw new ArgumentException("Two destinations overlap.", nameof(destinationY));
        }

        if (!InPlaceOrApart(x, written0) || !InPlaceOrApart(x, written1) || !InPlaceOrApart(x, written2)
            || !InPlaceOrApart(y, written0) || !InPlaceOrApart(y, written1) || !InPlaceOrApart(y, written2)
            || !InPlaceOrApart(z, written0) || !InPlaceOrApart(z, written1) || !InPlaceOrApart(z, written2))
        {
            throw new ArgumentException("A destination overlaps a source other than in place.", nameof(destinationX));
        }
    }

    private static bool InPlaceOrApart<TSource, TDestination>(ReadOnlySpan<TSource> source, ReadOnlySpan<TDestination> written)
        where TSource : unmanaged where TDestination : unmanaged =>
        !MemoryMarshal.AsBytes(source).Overlaps(MemoryMarshal.AsBytes(written), out var offset)
        || (offset == 0 && typeof(TSource) == typeof(TDestination));

    private static bool Overlap<T>(ReadOnlySpan<T> first, ReadOnlySpan<T> second) where T : unmanaged =>
        !first.IsEmpty && MemoryMarshal.AsBytes(first).Overlaps(MemoryMarshal.AsBytes(second));
}
