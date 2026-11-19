using System.Collections.Generic;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Fonts;

/// <summary>Finds the fonts a page uses: in its resources and their graphics states, the forms, tiling patterns and
/// soft-mask groups they hold, Type 3 fonts' own resources and its annotations' appearances - each font once, gone
/// through in a queue of its own. A resource dictionary is read once for every page of a document that shares it.
/// </summary>
internal sealed class PdfFontCollector
{
    /// <summary>The most resource dictionaries one page is searched through: a file built to nest without end stops
    /// here.</summary>
    public const int MaxResources = 10_000;

    private readonly PdfDocument _document;
    private readonly Dictionary<PdfDictionary, ResourceEntries> _read = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<PdfDictionary> _pending = new();
    private readonly HashSet<PdfDictionary> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PdfFont> _found = new(ReferenceEqualityComparer.Instance);

    public PdfFontCollector(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>The fonts <paramref name="page"/> uses, in the order they are met: its own resources first.</summary>
    public List<PdfFont> Collect(PdfPage page)
    {
        _pending.Clear();
        _seen.Clear();
        _found.Clear();
        var fonts = new List<PdfFont>();
        var processed = 0;

        // The page's content first, all of it, then what its annotations look like.
        Enqueue(page.Resources);
        Drain(fonts, ref processed);
        foreach (var annotation in page.Annotations)
        {
            EnqueueAppearances(annotation.Dictionary);
        }

        Drain(fonts, ref processed);
        return fonts;
    }

    private void Drain(List<PdfFont> fonts, ref int processed)
    {
        for (; _pending.Count > 0 && processed < MaxResources; processed++)
        {
            var entries = Read(_pending.Dequeue());
            foreach (var font in entries.Fonts)
            {
                if (_found.Add(font))
                {
                    fonts.Add(font);
                }
            }

            foreach (var inner in entries.Inner)
            {
                Enqueue(inner);
            }
        }
    }

    // What one resource dictionary holds directly: its fonts, and the resource dictionaries of what it draws with.
    private ResourceEntries Read(PdfDictionary resources)
    {
        if (_read.TryGetValue(resources, out var known))
        {
            return known;
        }

        var entries = new ResourceEntries();
        _read[resources] = entries;
        foreach (var entry in Entries(resources, PdfKeys.Font))
        {
            AddFont(entries, entry);
        }

        foreach (var entry in Entries(resources, PdfKeys.ExtGState))
        {
            if (_document.Resolve(entry) is not PdfDictionary state)
            {
                continue;
            }

            // A font and its size, as the state is applied; one without a size sets nothing.
            if (_document.Resolve(state[PdfGraphicsKeys.Font]) is PdfArray { Count: >= 2 } font)
            {
                AddFont(entries, font[0]);
            }

            if (_document.Resolve(state[PdfGraphicsKeys.SoftMask]) is PdfDictionary mask)
            {
                AddInner(entries, mask[PdfGraphicsKeys.Group]);
            }
        }

        // Forms among the XObjects, tiling patterns among the patterns: streams with resources of their own.
        foreach (var entry in Entries(resources, PdfKeys.XObject))
        {
            AddInner(entries, entry);
        }

        foreach (var entry in Entries(resources, PdfKeys.Pattern))
        {
            AddInner(entries, entry);
        }

        return entries;
    }

    private void AddFont(ResourceEntries entries, PdfObject value)
    {
        PdfFont font;
        try
        {
            font = PdfFonts.Create(value, _document);
        }
        catch (PdfException)
        {
            return;
        }

        if (font == null)
        {
            return;
        }

        entries.Fonts.Add(font);
        if (font is PdfType3Font { Resources.Dictionary: { } own })
        {
            entries.Inner.Add(own);
        }
    }

    private void AddInner(ResourceEntries entries, PdfObject value)
    {
        if (_document.Resolve(value) is PdfStream stream &&
            _document.Resolve(stream.Dictionary[PdfKeys.Resources]) is PdfDictionary resources)
        {
            entries.Inner.Add(resources);
        }
    }

    private IEnumerable<PdfObject> Entries(PdfDictionary resources, string category)
    {
        if (_document.Resolve(resources[category]) is not PdfDictionary entries)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            yield return entry.Value;
        }
    }

    // Every appearance - normal, rollover, down - and every state of each.
    private void EnqueueAppearances(PdfDictionary annotation)
    {
        if (_document.Resolve(annotation[PdfAnnotationKeys.Appearance]) is not PdfDictionary appearances)
        {
            return;
        }

        foreach (var kind in appearances)
        {
            switch (_document.Resolve(kind.Value))
            {
                case PdfStream stream:
                    Enqueue(_document.Resolve(stream.Dictionary[PdfKeys.Resources]) as PdfDictionary);
                    break;
                case PdfDictionary states:
                    foreach (var state in states)
                    {
                        if (_document.Resolve(state.Value) is PdfStream appearance)
                        {
                            Enqueue(_document.Resolve(appearance.Dictionary[PdfKeys.Resources]) as PdfDictionary);
                        }
                    }

                    break;
            }
        }
    }

    private void Enqueue(PdfDictionary resources)
    {
        if (resources != null && _seen.Add(resources))
        {
            _pending.Enqueue(resources);
        }
    }

    private sealed class ResourceEntries
    {
        public List<PdfFont> Fonts { get; } = [];

        public List<PdfDictionary> Inner { get; } = [];
    }
}
