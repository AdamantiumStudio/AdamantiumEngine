using System;
using System.Collections.Generic;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Fonts;

/// <summary>The metrics of a CIDFont by CID (ISO 32000-2, 9.7.4.3): <c>/W</c> widths, or <c>/W2</c> vertical metrics
/// of three numbers each, in runs of CIDs. Runs that overlap are cut so the one given later wins.</summary>
internal sealed class PdfCidMetrics
{
    private readonly List<(uint First, uint Last, uint Origin, double[] Values, bool Each)> _runs = [];
    private readonly int _size;

    /// <param name="size">Numbers each CID has: 1 for <c>/W</c>, 3 for <c>/W2</c>.</param>
    public PdfCidMetrics(PdfObject value, PdfDocument document, int size)
    {
        _size = size;
        if (document.Resolve(value) is not PdfArray array)
        {
            return;
        }

        var runs = new List<(uint First, uint Last, double[] Values, bool Each)>();
        for (var i = 0; i < array.Count;)
        {
            if (document.Resolve(array[i]) is not PdfInteger { Value: >= 0 and <= uint.MaxValue } first)
            {
                i++;
                continue;
            }

            if (i + 1 < array.Count && document.Resolve(array[i + 1]) is PdfArray each)
            {
                var values = PdfValues.Numbers(each, document) ?? [];
                var lastOfRun = first.Value + values.Length / size - 1;
                if (lastOfRun >= first.Value && lastOfRun <= uint.MaxValue)
                {
                    runs.Add(((uint)first.Value, (uint)lastOfRun, values, true));
                }

                i += 2;
                continue;
            }

            if (i + 1 + size < array.Count &&
                document.Resolve(array[i + 1]) is PdfInteger { Value: >= 0 and <= uint.MaxValue } last)
            {
                var values = new double[size];
                for (var k = 0; k < size; k++)
                {
                    values[k] = PdfValues.Number(array[i + 2 + k], document);
                }

                if (last.Value >= first.Value)
                {
                    runs.Add(((uint)first.Value, (uint)last.Value, values, false));
                }

                i += 2 + size;
                continue;
            }

            break;
        }

        var bounds = new List<(long Low, long High)>(runs.Count);
        foreach (var run in runs)
        {
            bounds.Add((run.First, run.Last));
        }

        foreach (var (low, high, index) in PdfRangeFlattener.Flatten(bounds))
        {
            var run = runs[index];
            _runs.Add(((uint)low, (uint)high, run.First, run.Values, run.Each));
        }
    }

    /// <summary>The numbers of <paramref name="cid"/>; empty when no run holds it.</summary>
    public ReadOnlySpan<double> Of(uint cid)
    {
        int low = 0, high = _runs.Count - 1, found = -1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (_runs[middle].First <= cid)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (found < 0 || _runs[found].Last < cid)
        {
            return default;
        }

        var run = _runs[found];
        return run.Each ? run.Values.AsSpan((int)(cid - run.Origin) * _size, _size) : run.Values;
    }
}
