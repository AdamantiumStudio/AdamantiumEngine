using System.Collections;
using System.Collections.Generic;

namespace Adamantium.Pdf.Objects;

/// <summary>An array. Items are as written: a reference stays a reference until the document resolves it.</summary>
public sealed class PdfArray : PdfObject, IReadOnlyList<PdfObject>
{
    private readonly List<PdfObject> _items;

    public PdfArray()
    {
        _items = [];
    }

    public PdfArray(List<PdfObject> items)
    {
        _items = items;
    }

    public int Count => _items.Count;

    public PdfObject this[int index] => _items[index];

    public void Add(PdfObject item) => _items.Add(item);

    public List<PdfObject>.Enumerator GetEnumerator() => _items.GetEnumerator();

    IEnumerator<PdfObject> IEnumerable<PdfObject>.GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
