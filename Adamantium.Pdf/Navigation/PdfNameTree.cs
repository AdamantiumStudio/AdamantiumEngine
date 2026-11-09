using System.Collections.Generic;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Navigation;

/// <summary>Name trees (ISO 32000-2, 7.9.6): string keys to values, through intermediate nodes, walked on an explicit
/// stack - a node, or an array of kids, met twice is taken once, and no more than <see cref="MaxEntries"/> entries and
/// <see cref="MaxSteps"/> kids are looked at.</summary>
internal static class PdfNameTree
{
    public const int MaxEntries = 1_000_000;
    public const int MaxSteps = 4_000_000;

    /// <summary>The entries under <paramref name="root"/>, in the tree's order.</summary>
    public static List<(string Key, PdfObject Value)> Entries(PdfObject root, PdfDocument document)
    {
        var entries = new List<(string, PdfObject)>();
        if (document.Resolve(root) is not PdfDictionary top)
        {
            return entries;
        }

        var visited = new HashSet<PdfObject>(ReferenceEqualityComparer.Instance) { top };
        var pending = new Stack<PdfDictionary>();
        pending.Push(top);
        var steps = 0;
        while (pending.Count > 0 && entries.Count < MaxEntries && steps < MaxSteps)
        {
            var node = pending.Pop();
            if (document.Resolve(node["Names"]) is PdfArray names && visited.Add(names))
            {
                for (var i = 0; i + 1 < names.Count && entries.Count < MaxEntries; i += 2)
                {
                    if (document.Resolve(names[i]) is PdfString key)
                    {
                        entries.Add((key.ToText(), names[i + 1]));
                    }
                }
            }

            if (document.Resolve(node["Kids"]) is PdfArray kids && visited.Add(kids))
            {
                for (var i = kids.Count - 1; i >= 0 && steps < MaxSteps; i--, steps++)
                {
                    if (document.Resolve(kids[i]) is PdfDictionary kid && visited.Add(kid))
                    {
                        pending.Push(kid);
                    }
                }
            }
        }

        return entries;
    }

    /// <summary>The entries under <paramref name="root"/> by key - the first of a key written twice.</summary>
    public static Dictionary<string, PdfObject> Map(PdfObject root, PdfDocument document)
    {
        var map = new Dictionary<string, PdfObject>();
        foreach (var (key, value) in Entries(root, document))
        {
            map.TryAdd(key, value);
        }

        return map;
    }
}
