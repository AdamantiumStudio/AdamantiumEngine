using System;
using System.Collections.Generic;

namespace Adamantium.Pdf.Fonts;

/// <summary>Cuts ranges that may overlap into pieces that do not, a later range covering an earlier one where they
/// meet - as a file read in order would overwrite it. O(n log n) for n ranges, in any order.</summary>
internal static class PdfRangeFlattener
{
    /// <returns>The pieces in order of value: their first and last value, and the index of the range they keep.
    /// </returns>
    public static List<(long Low, long High, int Index)> Flatten(IReadOnlyList<(long Low, long High)> ranges)
    {
        var points = new List<long>(ranges.Count * 2);
        foreach (var (low, high) in ranges)
        {
            points.Add(low);
            points.Add(high + 1);
        }

        points.Sort();
        var unique = 0;
        for (var i = 0; i < points.Count; i++)
        {
            if (unique == 0 || points[i] != points[unique - 1])
            {
                points[unique++] = points[i];
            }
        }

        points.RemoveRange(unique, points.Count - unique);
        var pieces = new List<(long Low, long High, int Index)>();
        var segments = points.Count - 1;
        if (segments <= 0)
        {
            return pieces;
        }

        var owner = new int[segments];
        Array.Fill(owner, -1);
        var nextFree = new int[segments + 1];
        for (var i = 0; i < nextFree.Length; i++)
        {
            nextFree[i] = i;
        }

        for (var r = ranges.Count - 1; r >= 0; r--)
        {
            var start = points.BinarySearch(ranges[r].Low);
            var end = points.BinarySearch(ranges[r].High + 1);
            for (var segment = Find(nextFree, start); segment < end; segment = Find(nextFree, segment + 1))
            {
                owner[segment] = r;
                nextFree[segment] = segment + 1;
            }
        }

        for (var segment = 0; segment < segments;)
        {
            var kept = owner[segment];
            var last = segment;
            while (last + 1 < segments && owner[last + 1] == kept)
            {
                last++;
            }

            if (kept >= 0)
            {
                pieces.Add((points[segment], points[last + 1] - 1, kept));
            }

            segment = last + 1;
        }

        return pieces;
    }

    private static int Find(int[] nextFree, int at)
    {
        var root = at;
        while (nextFree[root] != root)
        {
            root = nextFree[root];
        }

        while (nextFree[at] != root)
        {
            var next = nextFree[at];
            nextFree[at] = root;
            at = next;
        }

        return root;
    }
}
