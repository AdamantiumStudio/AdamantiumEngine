using System;
using System.Collections.Generic;
using System.Text;

namespace Adamantium.Pdf.Fonts;

/// <summary>The text of each CID of a character collection, read backwards from the collection's Unicode CMaps once
/// for the process - the data is the assembly's own - and safe to read from any thread; where several codes of one
/// CMap give one CID, its text is the shortest and lowest of them that compatibility normalization (NFKC) keeps as it
/// is - 一, not the radical ⼀ - else the shortest and lowest of the rest.</summary>
internal sealed class PdfCidTexts
{
    private static readonly PdfCidTexts None = new();
    private static readonly Lazy<PdfCidTexts> Japan1 = new(() => Read(PdfPredefinedCMaps.Japan1));
    private static readonly Lazy<PdfCidTexts> GB1 = new(() => Read(PdfPredefinedCMaps.GB1));
    private static readonly Lazy<PdfCidTexts> CNS1 = new(() => Read(PdfPredefinedCMaps.CNS1));
    private static readonly Lazy<PdfCidTexts> Korea1 = new(() => Read(PdfPredefinedCMaps.Korea1));

    private readonly List<(uint First, uint Last, uint Code, int Length)> _runs = [];

    private PdfCidTexts()
    {
    }

    /// <summary>The texts of an Adobe character collection; none for one the engine does not carry.</summary>
    public static PdfCidTexts Of(string ordering) => ordering switch
    {
        PdfPredefinedCMaps.Japan1 => Japan1.Value,
        PdfPredefinedCMaps.GB1 => GB1.Value,
        PdfPredefinedCMaps.CNS1 => CNS1.Value,
        PdfPredefinedCMaps.Korea1 => Korea1.Value,
        _ => None,
    };

    /// <summary>The text of <paramref name="cid"/>, or null when the collection's Unicode CMaps give it none.</summary>
    public string TextOf(uint cid)
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
            return null;
        }

        var run = _runs[found];
        return PdfCodeText.Utf16(run.Code + (cid - run.First), run.Length);
    }

    private static PdfCidTexts Read(string ordering)
    {
        var texts = new PdfCidTexts();
        var names = PdfPredefinedCMaps.UnicodeCMapsOf(ordering);
        var known = new Dictionary<string, PdfCMap>();
        var ranges = new List<PdfCodeRange>();
        var ofMap = new List<(PdfCodeRange Range, bool Compatibility)>();
        for (var i = names.Length - 1; i >= 0; i--)
        {
            if (PdfCMapReader.Predefined(names[i], known) is not { } map)
            {
                continue;
            }

            ofMap.Clear();
            foreach (var range in map.CidRanges)
            {
                ofMap.Add((range, IsCompatibility(range)));
            }

            ofMap.Sort(LosersFirst);
            foreach (var (range, _) in ofMap)
            {
                ranges.Add(range);
            }
        }

        var bounds = new List<(long Low, long High)>(ranges.Count);
        foreach (var range in ranges)
        {
            var first = (long)range.Start + (range.Low - range.Origin);
            bounds.Add((first, first + (range.High - range.Low)));
        }

        foreach (var (low, high, index) in PdfRangeFlattener.Flatten(bounds))
        {
            var range = ranges[index];
            var code = range.Low + (uint)(low - bounds[index].Low);
            texts._runs.Add(((uint)low, (uint)high, code, range.Length));
        }

        return texts;
    }

    private static int LosersFirst((PdfCodeRange Range, bool Compatibility) a,
        (PdfCodeRange Range, bool Compatibility) b)
    {
        if (a.Compatibility != b.Compatibility)
        {
            return a.Compatibility ? -1 : 1;
        }

        return a.Range.Length != b.Range.Length
            ? b.Range.Length.CompareTo(a.Range.Length)
            : b.Range.Low.CompareTo(a.Range.Low);
    }

    private static bool IsCompatibility(PdfCodeRange range) =>
        PdfCodeText.Utf16(range.Low, range.Length) is { } text &&
        !text.IsNormalized(NormalizationForm.FormKC);
}
