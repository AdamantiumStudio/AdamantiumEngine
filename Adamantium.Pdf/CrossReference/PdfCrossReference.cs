using System.Collections.Generic;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.CrossReference;

/// <summary>Where every object of the file is, and the trailer: the newest section wins over the ones it updates.
/// </summary>
public sealed class PdfCrossReference
{
    private readonly Dictionary<int, PdfCrossReferenceEntry> _entries = [];

    public PdfCrossReference(PdfDictionary trailer)
    {
        Trailer = trailer;
    }

    /// <summary>The trailer, its keys merged from every section, newest first.</summary>
    public PdfDictionary Trailer { get; }

    /// <summary>Whether the file's own cross-reference could not be used and this one was built by scanning.</summary>
    public bool IsRepaired { get; internal set; }

    public int Count => _entries.Count;

    public IEnumerable<int> Numbers => _entries.Keys;

    public bool TryGet(int number, out PdfCrossReferenceEntry entry) => _entries.TryGetValue(number, out entry);

    /// <summary>Adds the entry unless a newer section gave one already; whether it was added.</summary>
    internal bool AddOlder(int number, PdfCrossReferenceEntry entry) => _entries.TryAdd(number, entry);

    internal void Set(int number, PdfCrossReferenceEntry entry) => _entries[number] = entry;

    /// <summary>Takes the trailer keys an older section has and the newer ones lack.</summary>
    internal void MergeOlderTrailer(PdfDictionary older)
    {
        foreach (var (key, value) in older)
        {
            if (!Trailer.ContainsKey(key))
            {
                Trailer.Set(key, value);
            }
        }
    }
}
