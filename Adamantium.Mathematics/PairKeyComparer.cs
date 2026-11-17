using System.Collections.Generic;

namespace Adamantium.Mathematics;

/// <summary>Compares keys that pack two numbers into one 64-bit value - an edge's two vertices, a contact's two bodies -
/// hashed so that close pairs spread. A long's own hash folds its halves together, and such halves are close numbers,
/// so a dictionary of them fell into a few buckets and grew with the square of its size.</summary>
public sealed class PairKeyComparer : IEqualityComparer<long>, IEqualityComparer<ulong>
{
    private const ulong Spread = 0x9E3779B97F4A7C15;

    private PairKeyComparer()
    {
    }

    public static PairKeyComparer Instance { get; } = new();

    public bool Equals(long first, long second) => first == second;

    public int GetHashCode(long key) => GetHashCode((ulong)key);

    public bool Equals(ulong first, ulong second) => first == second;

    public int GetHashCode(ulong key) => (int)((key * Spread) >> 32);
}
