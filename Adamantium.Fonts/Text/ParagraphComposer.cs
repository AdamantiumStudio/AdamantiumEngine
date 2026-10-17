using System;
using System.Collections.Generic;

namespace Adamantium.Fonts.Text;

/// <summary>Breaks a whole paragraph into lines at once, the way TeX does (Knuth and Plass, "Breaking paragraphs into
/// lines", 1981): of all the ways to break it, the one whose lines are spaced most evenly, with the fewest hyphens, two
/// hyphenated lines in a row and a loose line beside a tight one costing extra.</summary>
public static class ParagraphComposer
{
    /// <summary>A penalty costing this much or more is never a place to end a line.</summary>
    public const double Never = 10000;

    /// <summary>A penalty costing this much or less ends a line, as the one ending a paragraph does.</summary>
    public const double Forced = -10000;

    private const double InfiniteBadness = 10000;
    private const double LinePenalty = 10;
    private const double DoubleHyphenDemerits = 10000;
    private const double FinalHyphenDemerits = 5000;
    private const double AdjacentDemerits = 10000;
    private const int VeryLoose = 0;
    private const int Loose = 1;
    private const int Decent = 2;
    private const int Tight = 3;

    /// <summary>The items <paramref name="items"/> breaks at, each line no worse than <paramref name="tolerance"/>
    /// (TeX's badness: 100 when its spaces stretch or shrink by all they may); null when no breaking keeps to it. The
    /// items end with a forced penalty, which is the last break. At most <paramref name="hyphenLimit"/> lines in a row
    /// end at a flagged penalty with a width - a hyphen the break adds - unless a line has no other place to break, a
    /// piece of a word wider than it; 0 sets no limit.</summary>
    public static int[] Break(IReadOnlyList<ComposerItem> items, double lineWidth, double tolerance, int hyphenLimit = 0)
    {
        return Run(items, [lineWidth], tolerance, 0, false, hyphenLimit);
    }

    /// <summary>The breaks of <see cref="Break(IReadOnlyList{ComposerItem}, double, double, int)"/> for lines of
    /// <paramref name="lineWidths"/> in turn, the last for every line after - narrower first lines beside a drop cap.</summary>
    public static int[] Break(IReadOnlyList<ComposerItem> items, IReadOnlyList<double> lineWidths, double tolerance,
        int hyphenLimit = 0)
    {
        return Run(items, lineWidths, tolerance, 0, false, hyphenLimit);
    }

    /// <summary>The breaks of <see cref="Break"/> as TeX's last pass finds them: every line may be as loose as it must,
    /// each glue stretching by <paramref name="emergencyStretch"/> more, and where nothing fits a line, as a word wider
    /// than it, the line overflows.</summary>
    public static int[] BreakAnyway(IReadOnlyList<ComposerItem> items, double lineWidth, double emergencyStretch,
        int hyphenLimit = 0)
    {
        return Run(items, [lineWidth], InfiniteBadness, emergencyStretch, true, hyphenLimit);
    }

    /// <summary>The breaks of <see cref="BreakAnyway(IReadOnlyList{ComposerItem}, double, double, int)"/> for lines of
    /// <paramref name="lineWidths"/> in turn, the last for every line after.</summary>
    public static int[] BreakAnyway(IReadOnlyList<ComposerItem> items, IReadOnlyList<double> lineWidths,
        double emergencyStretch, int hyphenLimit = 0)
    {
        return Run(items, lineWidths, InfiniteBadness, emergencyStretch, true, hyphenLimit);
    }

    private static int[] Run(IReadOnlyList<ComposerItem> items, IReadOnlyList<double> lineWidths, double tolerance,
        double emergencyStretch, bool lastPass, int hyphenLimit)
    {
        var count = items.Count;
        if (count == 0 || items[count - 1].Kind != ComposerItemKind.Penalty || items[count - 1].Cost > Forced)
        {
            throw new ArgumentException("A paragraph ends with a forced penalty.", nameof(items));
        }

        if (lineWidths == null || lineWidths.Count == 0)
        {
            throw new ArgumentException("There is a width for the lines.", nameof(lineWidths));
        }

        var lastWidth = lineWidths.Count - 1;

        var widths = new double[count + 1];
        var stretches = new double[count + 1];
        var shrinks = new double[count + 1];
        var fills = new int[count + 1];
        var plainBreaks = new int[count + 1];
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var isGlue = item.Kind == ComposerItemKind.Glue;
            var fillsLine = isGlue && double.IsPositiveInfinity(item.Stretch);
            var plainBreak = isGlue
                ? i > 0 && items[i - 1].Kind == ComposerItemKind.Box
                : item.Kind == ComposerItemKind.Penalty && item.Cost < Never && !item.Flagged;
            widths[i + 1] = widths[i] + (item.Kind == ComposerItemKind.Penalty ? 0 : item.Width);
            stretches[i + 1] = stretches[i] + (isGlue && !fillsLine ? item.Stretch : 0);
            shrinks[i + 1] = shrinks[i] + (isGlue ? item.Shrink : 0);
            fills[i + 1] = fills[i] + (fillsLine ? 1 : 0);
            plainBreaks[i + 1] = plainBreaks[i] + (plainBreak ? 1 : 0);
        }

        var firstBox = new int[count + 1];
        firstBox[count] = count;
        for (var i = count - 1; i >= 0; i--)
        {
            firstBox[i] = items[i].Kind == ComposerItemKind.Box ? i : firstBox[i + 1];
        }

        List<ComposerNode> active = [new ComposerNode(-1, 0, Decent, 0, null, false)];
        var candidates = new Dictionary<(int Line, int Fitness, int Hyphens), ComposerNode>();
        for (var b = 0; b < count; b++)
        {
            var item = items[b];
            var legal = item.Kind == ComposerItemKind.Glue
                ? b > 0 && items[b - 1].Kind == ComposerItemKind.Box
                : item.Kind == ComposerItemKind.Penalty && item.Cost < Never;
            if (!legal)
            {
                continue;
            }

            var isPenalty = item.Kind == ComposerItemKind.Penalty;
            var forced = isPenalty && item.Cost <= Forced;
            var last = b == count - 1;
            candidates.Clear();
            var found = false;
            for (var i = 0; i < active.Count;)
            {
                var from = active[i];
                var lineWidth = lineWidths[Math.Min(from.Line, lastWidth)];
                var lineKey = Math.Min(from.Line + 1, lastWidth);
                var start = from.Position < 0 ? 0 : firstBox[from.Position + 1];
                if (start > b)
                {
                    if (!forced)
                    {
                        i++;
                        continue;
                    }

                    var keep = lastPass && !found && active.Count == 1;
                    active.RemoveAt(i);
                    if (keep)
                    {
                        candidates[(lineKey, Decent, 0)] = new ComposerNode(b, from.Line + 1, Decent, from.Total, from, false);
                        found = true;
                    }

                    continue;
                }

                var length = widths[b] - widths[start] + (isPenalty ? item.Width : 0);
                var stretch = stretches[b] - stretches[start] + emergencyStretch;
                var shrink = shrinks[b] - shrinks[start];
                var fill = fills[b] - fills[start];
                var ratio = length < lineWidth
                    ? fill > 0 ? 0 : stretch > 0 ? (lineWidth - length) / stretch : double.PositiveInfinity
                    : length > lineWidth
                        ? shrink > 0 ? (lineWidth - length) / shrink : double.NegativeInfinity
                        : 0;
                var overfull = ratio < -1;
                var badness = overfull ? InfiniteBadness + 1 : Math.Min(100 * Math.Pow(Math.Abs(ratio), 3), InfiniteBadness);
                var artificial = false;
                if (overfull || forced)
                {
                    artificial = overfull && lastPass && !found && active.Count == 1;
                    active.RemoveAt(i);
                }
                else
                {
                    i++;
                }

                var hyphens = isPenalty && item.Flagged && item.Width > 0 ? from.Hyphens + 1 : 0;
                var overLimit = hyphenLimit > 0 && hyphens > hyphenLimit && plainBreaks[b] > plainBreaks[start];
                if ((badness > tolerance || overLimit) && !artificial)
                {
                    continue;
                }

                var fitness = ratio > 0
                    ? badness > 99 ? VeryLoose : badness > 12 ? Loose : Decent
                    : badness > 12 ? Tight : Decent;
                var demerits = from.Total;
                if (!artificial)
                {
                    var line = LinePenalty + badness;
                    demerits += Math.Abs(line) >= InfiniteBadness ? 1e8 : line * line;
                    if (isPenalty && item.Cost > 0)
                    {
                        demerits += item.Cost * item.Cost;
                    }
                    else if (isPenalty && item.Cost > Forced)
                    {
                        demerits -= item.Cost * item.Cost;
                    }

                    if (from.Flagged && (item.Flagged || last))
                    {
                        demerits += last ? FinalHyphenDemerits : DoubleHyphenDemerits;
                    }

                    if (Math.Abs(fitness - from.Fitness) > 1)
                    {
                        demerits += AdjacentDemerits;
                    }
                }

                var key = (lineKey, fitness, hyphenLimit > 0 ? hyphens : 0);
                if (!candidates.TryGetValue(key, out var known) || demerits < known.Total)
                {
                    candidates[key] = new ComposerNode(b, from.Line + 1, fitness, demerits, from, isPenalty && item.Flagged,
                        hyphens);
                    found = true;
                }
            }

            active.AddRange(candidates.Values);

            if (active.Count == 0)
            {
                return null;
            }
        }

        var best = active[0];
        foreach (var node in active)
        {
            if (node.Total < best.Total)
            {
                best = node;
            }
        }

        List<int> breaks = [];
        for (var node = best; node.Previous != null; node = node.Previous)
        {
            breaks.Add(node.Position);
        }

        breaks.Reverse();
        return breaks.ToArray();
    }
}
