using System;
using System.Collections;
using System.Collections.Generic;

namespace Adamantium.Pdf.Objects;

/// <summary>A dictionary, keyed by name without the slash. Values are as written: a reference stays a reference until
/// the document resolves it. A key whose value is null counts as absent, as the format says.</summary>
public sealed class PdfDictionary : PdfObject, IEnumerable<KeyValuePair<string, PdfObject>>
{
    private readonly Dictionary<string, PdfObject> _entries;

    public PdfDictionary()
    {
        _entries = new Dictionary<string, PdfObject>(StringComparer.Ordinal);
    }

    public int Count => _entries.Count;

    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>The value as written, or null when absent.</summary>
    public PdfObject this[string key] => _entries.GetValueOrDefault(key);

    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    public void Set(string key, PdfObject value)
    {
        if (value == null || value is PdfNull)
        {
            _entries.Remove(key);
            return;
        }

        _entries[key] = value;
    }

    public bool Remove(string key) => _entries.Remove(key);

    public Dictionary<string, PdfObject>.Enumerator GetEnumerator() => _entries.GetEnumerator();

    IEnumerator<KeyValuePair<string, PdfObject>> IEnumerable<KeyValuePair<string, PdfObject>>.GetEnumerator() =>
        _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();
}
