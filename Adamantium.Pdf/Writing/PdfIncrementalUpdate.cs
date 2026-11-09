using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.CrossReference;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Filters;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Parsing;

namespace Adamantium.Pdf.Writing;

/// <summary>Changes to a document written after its bytes (ISO 32000-2, 7.5.6): objects added, replaced or freed, the
/// file as it was left byte for byte. The new cross-reference section is of the kind the file's last one is - a table,
/// or a stream - and goes back to it by <c>/Prev</c>; a repaired file, whose own cross-reference cannot be trusted,
/// gets a whole one, a stream. New objects of an encrypted file are encrypted as its others are. The document is read
/// as it was opened: what the update holds is seen by opening what it writes.</summary>
public sealed class PdfIncrementalUpdate
{
    private readonly SortedDictionary<int, (PdfObjectId Id, PdfObject Value)> _written = [];
    private readonly SortedDictionary<int, int> _freed = [];
    private readonly Dictionary<int, PdfArray> _annotationArrays = [];
    private readonly Dictionary<int, PdfArray> _sharedAnnotationArrays = [];
    private int _next;

    public PdfIncrementalUpdate(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!document.IsAuthenticated)
        {
            throw new PdfException("The file is encrypted and no password has opened it.");
        }

        Document = document;
        _next = document.CrossReference.Numbers.DefaultIfEmpty(0).Max() + 1;
    }

    /// <summary>The largest object number a file may have (ISO 32000-2, annex C).</summary>
    public const int MaxObjectNumber = 8_388_607;

    private const string Popup = "Popup";

    public PdfDocument Document { get; }

    /// <summary>Whether anything was added, replaced or freed.</summary>
    public bool HasChanges => _written.Count > 0 || _freed.Count > 0;

    /// <summary>Adds <paramref name="value"/> as a new indirect object, and returns the reference to it.</summary>
    public PdfReference Add(PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_next >= MaxObjectNumber)
        {
            throw new PdfException("The file has as many objects as a file may have.");
        }

        var id = new PdfObjectId(_next++, 0);
        _written[id.Number] = (id, value);
        return new PdfReference(id);
    }

    /// <summary>Writes <paramref name="value"/> in place of the object <paramref name="id"/>.</summary>
    public void Replace(PdfObjectId id, PdfObject value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Check(id);
        _freed.Remove(id.Number);
        _written[id.Number] = (id, value);
        _next = Math.Max(_next, id.Number + 1);
    }

    /// <summary>Frees the object <paramref name="id"/>: references to it read as null afterwards.</summary>
    public void Remove(PdfObjectId id)
    {
        Check(id);
        _written.Remove(id.Number);
        _freed[id.Number] = Math.Min(id.Generation + 1, 65534);
        _next = Math.Max(_next, id.Number + 1);
    }

    private static void Check(PdfObjectId id)
    {
        if (id.Number <= 0 || id.Number >= MaxObjectNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "An object's number is 1 to " + (MaxObjectNumber - 1) +
                                                              "; 0 is the head of the free list.");
        }

        if (id.Generation is < 0 or > 65534)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "An object's generation is 0 to 65534.");
        }
    }

    /// <summary>The object this update writes as <paramref name="id"/>, or what the document has there.</summary>
    public PdfObject Get(PdfObjectId id) =>
        _written.TryGetValue(id.Number, out var written) && written.Id == id ? written.Value
        : _freed.ContainsKey(id.Number) ? PdfNull.Value
        : Document.Resolve(new PdfReference(id));

    /// <summary>Adds <paramref name="annotation"/> to <paramref name="page"/>, after its others, and returns the
    /// reference to it.</summary>
    public PdfReference AddAnnotation(PdfPage page, PdfNewAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        var annotations = AnnotationsOf(page);
        var appearance = annotation.Appearance != null ? Add(annotation.Appearance) : null;
        var reference = Add(annotation.ToDictionary(new PdfReference(page.Id!.Value), appearance));
        annotations.Add(reference);
        return reference;
    }

    /// <summary>Writes <paramref name="annotation"/> in place of <paramref name="existing"/>, an annotation of
    /// <paramref name="page"/>; the appearance it had goes with it, unless another annotation of the page shows it too,
    /// and its pop-up stays its own.</summary>
    public void ReplaceAnnotation(PdfPage page, PdfAnnotation existing, PdfNewAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(annotation);
        var id = existing.Id ?? throw new PdfException(
            "The annotation is written into the page's array, not as an object of its own; it cannot be replaced.");
        RemoveAppearances(AnnotationsOf(page), existing);
        var appearance = annotation.Appearance != null ? Add(annotation.Appearance) : null;
        var dictionary = annotation.ToDictionary(page.Id is { } pageId ? new PdfReference(pageId) : null, appearance);
        if (existing.Dictionary[Popup] is PdfReference popup)
        {
            dictionary.Set(Popup, popup);
        }

        Replace(id, dictionary);
    }

    /// <summary>Takes <paramref name="existing"/> off <paramref name="page"/> and frees it, with its appearance and its
    /// pop-up.</summary>
    public void RemoveAnnotation(PdfPage page, PdfAnnotation existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var annotations = AnnotationsOf(page);
        for (var i = annotations.Count - 1; i >= 0; i--)
        {
            var item = annotations[i];
            if (existing.Id is { } id ? item is PdfReference reference && reference.Id == id
                    : ReferenceEquals(item, existing.Dictionary))
            {
                annotations.RemoveAt(i);
            }
        }

        if (existing.Id is { } own)
        {
            RemoveAppearances(annotations, existing);
            if (existing.Dictionary[Popup] is PdfReference popup)
            {
                RemovePopup(annotations, popup.Id);
            }

            Remove(own);
        }
    }

    /// <summary>Declares in the catalog the developer extension of <paramref name="prefix"/> (ISO 32000-2, 7.12):
    /// the version it extends, its level and where it is described. Other levels of the same prefix, which PDF 2.0
    /// lists in an array, stay.</summary>
    public void DeclareExtension(string prefix, string baseVersion, int level, string url = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        ArgumentException.ThrowIfNullOrEmpty(baseVersion);
        var (id, catalog) = Catalog();
        var extensions = Copy(Document.Resolve(catalog[PdfKeys.Extensions]) as PdfDictionary ?? new PdfDictionary());
        var extension = new PdfDictionary();
        extension.Set(PdfKeys.Type, new PdfName("DeveloperExtensions"));
        extension.Set(PdfKeys.BaseVersion, new PdfName(baseVersion));
        extension.Set(PdfKeys.ExtensionLevel, new PdfInteger(level));
        if (url != null)
        {
            extension.Set(PdfKeys.Url, PdfString.FromText(url));
        }

        if (Document.Resolve(extensions[prefix]) is PdfArray levels)
        {
            var kept = new PdfArray();
            foreach (var item in levels)
            {
                if (PdfValues.Integer((Document.Resolve(item) as PdfDictionary)?[PdfKeys.ExtensionLevel], Document,
                        -1) != level)
                {
                    kept.Add(item);
                }
            }

            kept.Add(extension);
            extensions.Set(prefix, kept);
        }
        else
        {
            extensions.Set(prefix, extension);
        }

        catalog.Set(PdfKeys.Extensions, extensions);
        Replace(id, catalog);
    }

    private (PdfObjectId Id, PdfDictionary Catalog) Catalog()
    {
        var id = (Document.Trailer[PdfKeys.Root] as PdfReference)?.Id ?? throw new PdfException(
            "The file's catalog is not an object of its own; it cannot be changed.");
        return (id, Copy(Get(id) as PdfDictionary ?? throw new PdfException("The file has no catalog to change.")));
    }

    /// <summary>The file with the update after it.</summary>
    public byte[] Write()
    {
        using var output = new MemoryStream();
        Write(output);
        return output.ToArray();
    }

    /// <summary>Writes the file, then the update after it, to <paramref name="output"/>.</summary>
    public void Write(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var data = Document.Data.Span;
        using var section = new MemoryStream();
        if (data.Length > 0 && data[^1] is not ((byte)'\n' or (byte)'\r'))
        {
            section.WriteByte((byte)'\n');
        }

        var whole = Document.IsRepaired;
        var lastIsStream = false;
        var previous = whole ? -1 : LastStartXref(out lastIsStream);
        var stream = whole || lastIsStream;
        if (stream && VersionBelow(1, 5))
        {
            var (catalogId, catalog) = Catalog();
            catalog.Set(PdfKeys.Version, new PdfName("1.5"));
            Replace(catalogId, catalog);
        }

        var writer = new PdfObjectWriter(section, Document);
        var start = data.Length - (long)Document.HeaderOffset;
        var offsets = new SortedDictionary<int, (long Offset, int Generation)>();
        foreach (var (number, (id, value)) in _written)
        {
            offsets[number] = (start + section.Position, id.Generation);
            writer.WriteIndirect(id, value, false);
        }

        if (stream)
        {
            WriteXrefStream(writer, section, start, offsets, whole, previous);
        }
        else
        {
            WriteXrefTable(writer, section, start, offsets, previous);
        }

        output.Write(data);
        section.Position = 0;
        section.CopyTo(output);
    }

    private void WriteXrefTable(PdfObjectWriter writer, MemoryStream section, long start,
        SortedDictionary<int, (long Offset, int Generation)> offsets, long previous)
    {
        var at = start + section.Position;
        var entries = Entries(offsets);
        writer.Write("xref\n");
        foreach (var run in Runs(entries.Keys))
        {
            writer.Write($"{run.First} {run.Count}\n");
            for (var number = run.First; number < run.First + run.Count; number++)
            {
                var (kind, offset, generation) = entries[number];
                writer.Write($"{offset:D10} {generation:D5} {kind}\r\n");
            }
        }

        var trailer = Trailer(_next);
        trailer.Set(PdfKeys.Prev, new PdfInteger(previous));
        writer.Write("trailer\n");
        writer.WritePlain(trailer);
        writer.Write($"\nstartxref\n{at}\n%%EOF\n");
    }

    private void WriteXrefStream(PdfObjectWriter writer, MemoryStream section, long start,
        SortedDictionary<int, (long Offset, int Generation)> offsets, bool whole, long previous)
    {
        var number = _next;
        var size = number + 1;
        var at = start + section.Position;
        var rows = new SortedDictionary<int, (byte Type, long Field2, int Field3)>();
        if (whole)
        {
            rows[0] = (0, 0, 65535);
            foreach (var known in Document.CrossReference.Numbers)
            {
                if (Document.CrossReference.TryGet(known, out var entry))
                {
                    rows[known] = entry.Kind switch
                    {
                        PdfCrossReferenceEntryKind.InFile => (1, entry.Offset - Document.HeaderOffset,
                            entry.Generation),
                        PdfCrossReferenceEntryKind.InObjectStream => (2, entry.StreamNumber, entry.Index),
                        _ => (0, 0, entry.Generation),
                    };
                }
            }
        }

        foreach (var (key, (kind, offset, generation)) in Entries(offsets))
        {
            rows[key] = kind == 'n' ? ((byte)1, offset, generation) : ((byte)0, offset, generation);
        }

        rows[number] = (1, at, 0);
        var offsetBytes = Math.Max(1, BytesFor(rows.Values.Max(row => row.Field2)));
        var thirdBytes = Math.Max(1, BytesFor(rows.Values.Max(row => (long)row.Field3)));
        var table = new MemoryStream();
        foreach (var (type, field2, field3) in rows.Values)
        {
            table.WriteByte(type);
            WriteBigEndian(table, field2, offsetBytes);
            WriteBigEndian(table, field3, thirdBytes);
        }

        var index = new PdfArray();
        foreach (var run in Runs(rows.Keys))
        {
            index.Add(new PdfInteger(run.First));
            index.Add(new PdfInteger(run.Count));
        }

        var dictionary = Trailer(size);
        dictionary.Set(PdfKeys.Type, new PdfName(PdfObjectTypes.XRef));
        dictionary.Set(PdfKeys.W, new PdfArray([new PdfInteger(1), new PdfInteger(offsetBytes),
            new PdfInteger(thirdBytes)]));
        dictionary.Set(PdfKeys.Index, index);
        dictionary.Set(PdfKeys.Filter, new PdfName(PdfFilterNames.FlateDecode));
        if (previous >= 0)
        {
            dictionary.Set(PdfKeys.Prev, new PdfInteger(previous));
        }

        writer.WriteIndirect(new PdfObjectId(number, 0), new PdfStream(dictionary, Deflate(table.ToArray())), true);
        writer.Write($"startxref\n{at}\n%%EOF\n");
    }

    private PdfArray AnnotationsOf(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var id = page.Id ?? throw new PdfException(
            "The page is written into its parent, not as an object of its own; it cannot be changed.");
        if (_annotationArrays.TryGetValue(id.Number, out var known))
        {
            return known;
        }

        var dictionary = Copy(Get(id) as PdfDictionary ?? page.Dictionary);
        PdfArray annotations;
        if (dictionary[PdfKeys.Annots] is PdfReference reference && Get(reference.Id) is PdfArray shared)
        {
            if (!_sharedAnnotationArrays.TryGetValue(reference.Id.Number, out annotations))
            {
                annotations = Copy(shared);
                Replace(reference.Id, annotations);
                _sharedAnnotationArrays[reference.Id.Number] = annotations;
            }
        }
        else
        {
            annotations = Document.Resolve(dictionary[PdfKeys.Annots]) is PdfArray direct
                ? Copy(direct)
                : new PdfArray();
            dictionary.Set(PdfKeys.Annots, annotations);
            Replace(id, dictionary);
        }

        _annotationArrays[id.Number] = annotations;
        return annotations;
    }

    private void RemoveAppearances(PdfArray annotations, PdfAnnotation annotation)
    {
        var shown = new HashSet<PdfObjectId>();
        foreach (var item in annotations)
        {
            if (item is PdfReference other && other.Id == annotation.Id)
            {
                continue;
            }

            if (Document.Resolve(item) is PdfDictionary dictionary && !ReferenceEquals(dictionary, annotation.Dictionary))
            {
                shown.UnionWith(AppearanceStreams(dictionary));
            }
        }

        foreach (var stream in AppearanceStreams(annotation.Dictionary))
        {
            if (!shown.Contains(stream) && Get(stream) is PdfStream)
            {
                Remove(stream);
            }
        }
    }

    private List<PdfObjectId> AppearanceStreams(PdfDictionary annotation)
    {
        var streams = new List<PdfObjectId>();
        if (Document.Resolve(annotation["AP"]) is not PdfDictionary appearances)
        {
            return streams;
        }

        foreach (var (_, value) in appearances)
        {
            if (value is PdfReference stream)
            {
                streams.Add(stream.Id);
            }

            if (Document.Resolve(value) is PdfDictionary states)
            {
                foreach (var (_, state) in states)
                {
                    if (state is PdfReference stateStream)
                    {
                        streams.Add(stateStream.Id);
                    }
                }
            }
        }

        return streams;
    }

    private void RemovePopup(PdfArray annotations, PdfObjectId popup)
    {
        for (var i = annotations.Count - 1; i >= 0; i--)
        {
            if (annotations[i] is PdfReference reference && reference.Id == popup)
            {
                annotations.RemoveAt(i);
            }
        }

        Remove(popup);
    }

    private static PdfDictionary Copy(PdfDictionary dictionary)
    {
        var copy = new PdfDictionary();
        foreach (var (key, value) in dictionary)
        {
            copy.Set(key, value);
        }

        return copy;
    }

    private static PdfArray Copy(PdfArray array)
    {
        var copy = new PdfArray();
        foreach (var item in array)
        {
            copy.Add(item);
        }

        return copy;
    }

    private SortedDictionary<int, (char Kind, long Offset, int Generation)> Entries(
        SortedDictionary<int, (long Offset, int Generation)> offsets)
    {
        var entries = new SortedDictionary<int, (char Kind, long Offset, int Generation)>();
        foreach (var (number, (offset, generation)) in offsets)
        {
            entries[number] = ('n', offset, generation);
        }

        var freed = _freed.Keys.ToList();
        for (var i = 0; i < freed.Count; i++)
        {
            entries[freed[i]] = ('f', i + 1 < freed.Count ? freed[i + 1] : 0, _freed[freed[i]]);
        }

        return entries;
    }

    private PdfDictionary Trailer(int size)
    {
        var trailer = new PdfDictionary();
        trailer.Set(PdfKeys.Size, new PdfInteger(size));
        foreach (var key in new[] { PdfKeys.Root, PdfKeys.Info, PdfKeys.Id, PdfKeys.Encrypt })
        {
            if (Document.Trailer[key] is { } value)
            {
                trailer.Set(key, value);
            }
        }

        return trailer;
    }

    private static IEnumerable<(int First, int Count)> Runs(IEnumerable<int> numbers)
    {
        var first = -1;
        var count = 0;
        foreach (var number in numbers)
        {
            if (count > 0 && number == first + count)
            {
                count++;
                continue;
            }

            if (count > 0)
            {
                yield return (first, count);
            }

            (first, count) = (number, 1);
        }

        if (count > 0)
        {
            yield return (first, count);
        }
    }

    private long LastStartXref(out bool isStream)
    {
        var data = Document.Data;
        var at = data.Span.LastIndexOf(PdfKeywords.StartXrefBytes);
        if (at < 0)
        {
            throw new PdfException("The file has no startxref to continue from.");
        }

        var token = new PdfLexer(data) { Position = at + PdfKeywords.StartXref.Length }.Next();
        var offset = token.Integer;
        if (token.Kind != PdfTokenKind.Integer || offset < 0)
        {
            throw new PdfException("The file's startxref has no offset to continue from.");
        }

        isStream = true;
        foreach (var position in new[] { offset, offset + Document.HeaderOffset })
        {
            if (position >= 0 && position < data.Length)
            {
                var section = data.Span[(int)position..].TrimStart(" \t\r\n\f\0"u8);
                if (section.StartsWith("xref"u8))
                {
                    isStream = false;
                    break;
                }
            }
        }

        return offset;
    }

    private bool VersionBelow(int major, int minor)
    {
        var parts = (Document.Version ?? "1.0").Split('.');
        var (actualMajor, actualMinor) = (parts.Length > 0 && int.TryParse(parts[0], out var a) ? a : 1,
            parts.Length > 1 && int.TryParse(parts[1], out var b) ? b : 0);
        return actualMajor < major || (actualMajor == major && actualMinor < minor);
    }

    private static int BytesFor(long value)
    {
        var bytes = 0;
        for (; value > 0; value >>= 8)
        {
            bytes++;
        }

        return bytes;
    }

    private static void WriteBigEndian(Stream output, long value, int bytes)
    {
        for (var i = bytes - 1; i >= 0; i--)
        {
            output.WriteByte((byte)(value >> (8 * i)));
        }
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }
}
