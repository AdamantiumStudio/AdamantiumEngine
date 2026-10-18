using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Fonts.Text;
using NUnit.Framework;

namespace Adamantium.FontTests;

/// <summary>The paragraph composer against every way to break small paragraphs: it finds the breaking with the
/// fewest demerits, and only when one keeps to the tolerance. The demerits are TeX's rules written out again: badness
/// and fitness of each line, the line penalty 10, a hyphen after a hyphen 10000, a hyphen ending the next to last line
/// 5000, fitness classes two apart 10000.</summary>
public class ParagraphComposerTests
{
    private const double Fill = double.PositiveInfinity;

    [Test]
    public void Break_FindsTheBreakingWithTheFewestDemerits([Range(0, 399)] int seed)
    {
        var random = new Random(seed);
        var items = RandomParagraph(random);
        double width = random.Next(120, 260);
        List<double> widths = random.Next(2) == 0
            ? [width]
            : [width - random.Next(20, 80), width - random.Next(20, 80), width];
        var tolerance = random.Next(3) switch { 0 => 100, 1 => 200, _ => 1000 };

        var breaks = ParagraphComposer.Break(items, widths, tolerance);
        var best = BestByEveryBreaking(items, widths, tolerance);

        if (best == null)
        {
            Assert.That(breaks, Is.Null, "no breaking keeps to the tolerance");
            return;
        }

        Assert.That(breaks, Is.Not.Null);
        var demerits = Demerits(items, widths, tolerance, breaks);
        Assert.That(demerits, Is.Not.Null, "every line keeps to the tolerance");
        Assert.That(demerits.Value, Is.EqualTo(best.Value).Within(1e-6 * Math.Max(1, best.Value)));
    }

    [Test]
    public void Break_WithAHyphenLimit_FindsTheBestBreakingWithinIt([Range(0, 199)] int seed)
    {
        var random = new Random(seed);
        var items = RandomParagraph(random, splitOneIn: 1);
        List<double> widths = [random.Next(100, 200)];
        var limit = 1 + seed % 2;

        var breaks = ParagraphComposer.Break(items, widths, 1000, limit);
        var best = BestByEveryBreaking(items, widths, 1000, limit);

        if (best == null)
        {
            Assert.That(breaks, Is.Null, "no breaking keeps to the tolerance and the limit");
            return;
        }

        Assert.That(breaks, Is.Not.Null);
        var demerits = Demerits(items, widths, 1000, breaks, limit);
        Assert.That(demerits, Is.Not.Null, "no more lines in a row end in a hyphen than the limit");
        Assert.That(demerits.Value, Is.EqualTo(best.Value).Within(1e-6 * Math.Max(1, best.Value)));
    }

    // A hyphen at a break whose halves take less than the word did still counts toward the limit.
    [Test]
    public void Break_WithAHyphenLimit_CountsHyphensThatNarrowTheLine([Range(0, 99)] int seed)
    {
        var random = new Random(seed);
        var items = RandomParagraph(random, splitOneIn: 1, narrowing: true);
        List<double> widths = [random.Next(100, 200)];

        var breaks = ParagraphComposer.Break(items, widths, 1000, 1);
        var best = BestByEveryBreaking(items, widths, 1000, 1);

        if (best == null)
        {
            Assert.That(breaks, Is.Null);
            return;
        }

        Assert.That(breaks, Is.Not.Null);
        Assert.That(Demerits(items, widths, 1000, breaks, 1), Is.Not.Null, "no two lines in a row end in a hyphen");
    }

    [Test]
    public void BreakAnyway_SetsAWordWiderThanTheLineOnALineOfItsOwn()
    {
        List<ComposerItem> items =
        [
            ComposerItem.Box(40), ComposerItem.Glue(10, 5, 3), ComposerItem.Box(500), ComposerItem.Glue(10, 5, 3),
            ComposerItem.Box(40), ComposerItem.Glue(0, Fill, 0), ComposerItem.Penalty(0, ParagraphComposer.Forced, false),
        ];

        Assert.That(ParagraphComposer.Break(items, 100, 10000), Is.Null);
        Assert.That(ParagraphComposer.BreakAnyway(items, 100, 0), Is.EqualTo(new[] { 1, 3, 6 }));
    }

    [Test]
    public void BreakAnyway_BreaksAParagraphEndingInAWordWiderThanTheLine()
    {
        List<ComposerItem> guarded =
        [
            ComposerItem.Box(40), ComposerItem.Glue(10, 5, 3), ComposerItem.Box(500),
            ComposerItem.Penalty(0, ParagraphComposer.Never, false), ComposerItem.Glue(0, Fill, 0),
            ComposerItem.Penalty(0, ParagraphComposer.Forced, false),
        ];
        List<ComposerItem> bare = [ComposerItem.Box(500), ComposerItem.Glue(0, Fill, 0), ComposerItem.Penalty(0, ParagraphComposer.Forced, false)];

        Assert.That(ParagraphComposer.BreakAnyway(guarded, 100, 40), Is.EqualTo(new[] { 1, 5 }));
        Assert.That(ParagraphComposer.BreakAnyway(bare, 100, 40), Is.Not.Null);
    }

    // A break whose rest of the word takes more set by itself makes the next line that much longer: here too long.
    [TestCase(0, true)]
    [TestCase(20, false)]
    public void APenaltysAfter_LengthensTheNextLine(double after, bool breaks)
    {
        List<ComposerItem> items =
        [
            ComposerItem.Box(60), ComposerItem.Penalty(10, 0, true, after), ComposerItem.Box(60),
            ComposerItem.Glue(0, Fill, 0), ComposerItem.Penalty(0, ParagraphComposer.Forced, false),
        ];

        var found = ParagraphComposer.Break(items, 70, 100);

        Assert.That(found, breaks ? Is.EqualTo(new[] { 1, 4 }) : Is.Null);
    }

    [Test]
    public void AnEmptyParagraph_IsOneLine()
    {
        List<ComposerItem> items = [ComposerItem.Glue(0, Fill, 0), ComposerItem.Penalty(0, ParagraphComposer.Forced, false)];

        Assert.That(ParagraphComposer.Break(items, 100, 100), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void AParagraphWithoutAForcedEnd_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => ParagraphComposer.Break([ComposerItem.Box(10)], 100, 100));
    }

    private static List<ComposerItem> RandomParagraph(Random random, int splitOneIn = 3, bool narrowing = false)
    {
        List<ComposerItem> items = [];
        var words = random.Next(4, 10);
        for (var w = 0; w < words; w++)
        {
            if (w > 0)
            {
                if (random.Next(6) == 0)
                {
                    items.Add(ComposerItem.Penalty(0, random.Next(-100, 200), false));
                }
                else
                {
                    var space = random.Next(6, 14);
                    items.Add(ComposerItem.Glue(space, space / 2.0, space / 3.0));
                }
            }

            var length = random.Next(10, 90);
            if (length > 40 && random.Next(splitOneIn) == 0)
            {
                var head = random.Next(10, length - 10);
                items.Add(ComposerItem.Box(head));
                // A word broken inside a ligature set again in halves: the line may come out narrower for the hyphen.
                items.Add(narrowing ? ComposerItem.Penalty(-2, 50, true, 0) : ComposerItem.Penalty(6, 50, true));
                items.Add(ComposerItem.Box(length - head));
            }
            else
            {
                items.Add(ComposerItem.Box(length));
            }
        }

        items.Add(ComposerItem.Glue(0, Fill, 0));
        items.Add(ComposerItem.Penalty(0, ParagraphComposer.Forced, false));
        return items;
    }

    private static double? BestByEveryBreaking(List<ComposerItem> items, IReadOnlyList<double> widths, double tolerance,
        int hyphenLimit = 0)
    {
        var legal = Enumerable.Range(0, items.Count - 1).Where(i => IsLegal(items, i)).ToArray();
        Assert.That(legal.Length, Is.LessThanOrEqualTo(20), "small enough to try every breaking");
        double? best = null;
        for (var mask = 0; mask < 1 << legal.Length; mask++)
        {
            List<int> breaks = [];
            for (var k = 0; k < legal.Length; k++)
            {
                if ((mask & (1 << k)) != 0)
                {
                    breaks.Add(legal[k]);
                }
            }

            breaks.Add(items.Count - 1);
            var demerits = Demerits(items, widths, tolerance, breaks, hyphenLimit);
            if (demerits != null && (best == null || demerits < best))
            {
                best = demerits;
            }
        }

        return best;
    }

    private static bool IsLegal(List<ComposerItem> items, int i)
    {
        return items[i].Kind switch
        {
            ComposerItemKind.Glue => i > 0 && items[i - 1].Kind == ComposerItemKind.Box,
            ComposerItemKind.Penalty => items[i].Cost < ParagraphComposer.Never,
            _ => false,
        };
    }

    private static double? Demerits(List<ComposerItem> items, IReadOnlyList<double> widths, double tolerance,
        IReadOnlyList<int> breaks, int hyphenLimit = 0)
    {
        double total = 0;
        var previous = -1;
        var previousFitness = 2;
        var previousFlagged = false;
        var hyphens = 0;
        var lineIndex = 0;
        foreach (var b in breaks)
        {
            var width = widths[Math.Min(lineIndex++, widths.Count - 1)];
            var start = 0;
            if (previous >= 0)
            {
                start = previous + 1;
                while (start < items.Count && items[start].Kind != ComposerItemKind.Box)
                {
                    start++;
                }

                if (start > b)
                {
                    return null;
                }
            }

            double length = 0;
            double stretch = 0;
            double shrink = 0;
            var fill = false;
            for (var i = start; i < b; i++)
            {
                if (items[i].Kind == ComposerItemKind.Penalty)
                {
                    continue;
                }

                length += items[i].Width;
                if (items[i].Kind == ComposerItemKind.Glue)
                {
                    fill |= double.IsPositiveInfinity(items[i].Stretch);
                    stretch += double.IsPositiveInfinity(items[i].Stretch) ? 0 : items[i].Stretch;
                    shrink += items[i].Shrink;
                }
            }

            var item = items[b];
            if (item.Kind == ComposerItemKind.Penalty)
            {
                length += item.Width;
            }

            double badness;
            int fitness;
            if (length < width)
            {
                var ratio = fill ? 0 : stretch > 0 ? (width - length) / stretch : double.PositiveInfinity;
                badness = Math.Min(100 * Math.Pow(ratio, 3), 10000);
                fitness = badness > 99 ? 0 : badness > 12 ? 1 : 2;
            }
            else if (length > width)
            {
                var ratio = shrink > 0 ? (width - length) / shrink : double.NegativeInfinity;
                if (ratio < -1)
                {
                    return null;
                }

                badness = Math.Min(100 * Math.Pow(-ratio, 3), 10000);
                fitness = badness > 12 ? 3 : 2;
            }
            else
            {
                badness = 0;
                fitness = 2;
            }

            if (badness > tolerance)
            {
                return null;
            }

            var last = b == items.Count - 1;
            var line = 10 + badness;
            var demerits = line >= 10000 ? 1e8 : line * line;
            if (item.Kind == ComposerItemKind.Penalty && item.Cost > 0)
            {
                demerits += item.Cost * item.Cost;
            }
            else if (item.Kind == ComposerItemKind.Penalty && item.Cost > ParagraphComposer.Forced)
            {
                demerits -= item.Cost * item.Cost;
            }

            var flagged = item.Kind == ComposerItemKind.Penalty && item.Flagged;
            if (previousFlagged && (flagged || last))
            {
                demerits += last ? 5000 : 10000;
            }

            if (Math.Abs(fitness - previousFitness) > 1)
            {
                demerits += 10000;
            }

            hyphens = flagged && (item.Width > 0 || item.Discretionary) ? hyphens + 1 : 0;
            if (hyphenLimit > 0 && hyphens > hyphenLimit && Enumerable.Range(start, b - start).Any(i =>
                    items[i].Kind == ComposerItemKind.Glue
                        ? i > 0 && items[i - 1].Kind == ComposerItemKind.Box
                        : items[i].Kind == ComposerItemKind.Penalty && items[i].Cost < ParagraphComposer.Never
                                                                    && !items[i].Flagged))
            {
                return null;
            }

            total += demerits;
            previous = b;
            previousFitness = fitness;
            previousFlagged = flagged;
        }

        return total;
    }
}
