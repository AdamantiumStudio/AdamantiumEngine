using System.Collections.Generic;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.MathTests;

/// <summary>Keys packing two close numbers - a mesh's edges, a world's contacts - spread over a dictionary's buckets.
/// </summary>
public class PairKeyComparerTests
{
    [Test]
    public void ThePairsOfNearbyNumbers_HashApart_WhereALongsOwnHashFoldsThemTogether()
    {
        var spread = new HashSet<int>();
        var folded = new HashSet<int>();
        var pairs = 0;
        for (var lower = 0; lower < 200; lower++)
        {
            for (var higher = lower + 1; higher < 200; higher++)
            {
                var key = ((long)lower << 32) | (uint)higher;
                spread.Add(PairKeyComparer.Instance.GetHashCode(key));
                folded.Add(key.GetHashCode());
                pairs++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(folded.Count, Is.LessThanOrEqualTo(256), "a long's own hash is the halves folded together");
            Assert.That(spread.Count, Is.GreaterThan(pairs * 0.99), "close pairs shared hashes");
            Assert.That(PairKeyComparer.Instance.GetHashCode((ulong)7 << 32 | 9),
                Is.EqualTo(PairKeyComparer.Instance.GetHashCode((long)7 << 32 | 9)), "a long and an ulong key hash alike");
        });
    }
}
