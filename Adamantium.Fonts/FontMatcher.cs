using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.Fonts;

namespace Adamantium.Fonts;

internal static class FontMatcher
{
    public static FontFace Match(IReadOnlyCollection<FontFace> faces, FontWeight weight, FontStyle style,
        FontStretch stretch)
    {
        if (faces == null || faces.Count == 0)
        {
            return null;
        }

        var candidates = ByStretch(faces.ToList(), stretch.Percent);
        candidates = ByStyle(candidates, style);
        candidates = ByWeight(candidates, weight.Value);
        return candidates
            .OrderBy(f => f.IsVariable)
            .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.CollectionIndex)
            .First();
    }

    private static List<FontFace> ByStretch(List<FontFace> faces, double desired)
    {
        var chosen = Nearest(faces, desired, f => f.MinStretch.Percent, f => f.MaxStretch.Percent,
            desired <= 100 ? Direction.DownThenUp : Direction.UpThenDown);
        return faces.Where(f => f.MinStretch.Percent <= chosen && chosen <= f.MaxStretch.Percent).ToList();
    }

    private static List<FontFace> ByStyle(List<FontFace> faces, FontStyle desired)
    {
        FontStyle[] order = desired switch
        {
            FontStyle.Italic => [FontStyle.Italic, FontStyle.Oblique, FontStyle.Normal],
            FontStyle.Oblique => [FontStyle.Oblique, FontStyle.Italic, FontStyle.Normal],
            _ => [FontStyle.Normal, FontStyle.Oblique, FontStyle.Italic],
        };
        foreach (var style in order)
        {
            var matching = faces.Where(f => f.Style == style).ToList();
            if (matching.Count > 0)
            {
                return matching;
            }
        }

        return faces;
    }

    private static List<FontFace> ByWeight(List<FontFace> faces, int desired)
    {
        double chosen;
        if (desired is >= 400 and <= 500)
        {
            var upTo500 = faces.Where(f => f.MinWeight.Value > desired && f.MinWeight.Value <= 500).ToList();
            chosen = faces.Any(f => Covers(f, desired))
                ? desired
                : upTo500.Count > 0
                    ? upTo500.Min(f => f.MinWeight.Value)
                    : Nearest(faces, desired, f => f.MinWeight.Value, f => f.MaxWeight.Value, Direction.DownThenUp);
        }
        else
        {
            chosen = Nearest(faces, desired, f => f.MinWeight.Value, f => f.MaxWeight.Value,
                desired < 400 ? Direction.DownThenUp : Direction.UpThenDown);
        }

        return faces.Where(f => f.MinWeight.Value <= chosen && chosen <= f.MaxWeight.Value).ToList();
    }

    private static bool Covers(FontFace face, int weight) =>
        face.MinWeight.Value <= weight && weight <= face.MaxWeight.Value;

    private static double Nearest(List<FontFace> faces, double desired, Func<FontFace, double> min,
        Func<FontFace, double> max, Direction direction)
    {
        if (faces.Any(f => min(f) <= desired && desired <= max(f)))
        {
            return desired;
        }

        var below = faces.Where(f => max(f) < desired).Select(max).DefaultIfEmpty(double.NaN).Max();
        var above = faces.Where(f => min(f) > desired).Select(min).DefaultIfEmpty(double.NaN).Min();
        if (direction == Direction.DownThenUp)
        {
            return double.IsNaN(below) ? above : below;
        }

        return double.IsNaN(above) ? below : above;
    }

    private enum Direction
    {
        DownThenUp,
        UpThenDown,
    }
}
