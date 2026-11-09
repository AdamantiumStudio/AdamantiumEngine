using System;
using System.Collections.Generic;
using Adamantium.Pdf.Annotations;
using Adamantium.Pdf.Measurement;
using Adamantium.Pdf.Navigation;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Text;

namespace Adamantium.Pdf.Document;

/// <summary>One page: its boxes, rotation, resources and content streams, inherited attributes already applied.
/// </summary>
public sealed class PdfPage
{
    private static readonly PdfRectangle Letter = new(0, 0, 612, 792);

    private readonly PdfDocument _document;
    private readonly PdfInheritedAttributes _inherited;

    internal PdfPage(PdfDocument document, int index, PdfDictionary dictionary, PdfInheritedAttributes inherited)
    {
        _document = document;
        _inherited = inherited;
        Index = index;
        Dictionary = dictionary;
        MediaBox = PdfRectangle.From(inherited.MediaBox, document) is { IsEmpty: false } media ? media : Letter;
        CropBox = Clip(PdfRectangle.From(inherited.CropBox, document), MediaBox);
        BleedBox = Clip(PdfRectangle.From(dictionary[PdfKeys.BleedBox], document), CropBox);
        TrimBox = Clip(PdfRectangle.From(dictionary[PdfKeys.TrimBox], document), CropBox);
        ArtBox = Clip(PdfRectangle.From(dictionary[PdfKeys.ArtBox], document), CropBox);
        Rotation = NormalizeRotation(document.Resolve(inherited.Rotate));
        UserUnit = document.Resolve(dictionary[PdfKeys.UserUnit]) switch
        {
            PdfInteger integer when integer.Value > 0 => integer.Value,
            PdfReal real when real.Value > 0 && double.IsFinite(real.Value) => real.Value,
            _ => 1,
        };
    }

    /// <summary>Where the page stands in the document, from zero.</summary>
    public int Index { get; }

    /// <summary>The page's object, through which it is changed (<see cref="Writing.PdfIncrementalUpdate"/>); null for
    /// a page written directly into its parent, which a file should not do.</summary>
    public PdfObjectId? Id { get; internal init; }

    public PdfDictionary Dictionary { get; }

    public PdfRectangle MediaBox { get; }

    /// <summary>The visible region; the media box when not given, and never outside it.</summary>
    public PdfRectangle CropBox { get; }

    public PdfRectangle BleedBox { get; }

    public PdfRectangle TrimBox { get; }

    public PdfRectangle ArtBox { get; }

    /// <summary>The clockwise rotation to show it at: 0, 90, 180 or 270.</summary>
    public int Rotation { get; }

    /// <summary>The size of a unit of default user space in points.</summary>
    public double UserUnit { get; }

    /// <summary>The shown width in points: the crop box scaled by <see cref="UserUnit"/>, turned by
    /// <see cref="Rotation"/>.</summary>
    public double Width => (Rotation % 180 == 0 ? CropBox.Width : CropBox.Height) * UserUnit;

    /// <summary>The shown height in points.</summary>
    public double Height => (Rotation % 180 == 0 ? CropBox.Height : CropBox.Width) * UserUnit;

    public PdfDictionary Resources => _document.Resolve(_inherited.Resources) as PdfDictionary ?? new PdfDictionary();

    /// <summary>The content streams in order; an array of them is one stream cut in pieces.</summary>
    public IReadOnlyList<PdfStream> ContentStreams
    {
        get
        {
            var streams = new List<PdfStream>();
            switch (_document.Resolve(Dictionary[PdfKeys.Contents]))
            {
                case PdfStream single:
                    streams.Add(single);
                    break;
                case PdfArray array:
                    foreach (var item in array)
                    {
                        if (_document.Resolve(item) is PdfStream stream)
                        {
                            streams.Add(stream);
                        }
                    }

                    break;
            }

            return streams;
        }
    }

    /// <summary>The page's annotations (<c>/Annots</c>), in their order; entries that are not dictionaries left out.
    /// </summary>
    public IReadOnlyList<PdfAnnotation> Annotations
    {
        get
        {
            var annotations = new List<PdfAnnotation>();
            if (_document.Resolve(Dictionary[PdfKeys.Annots]) is PdfArray array)
            {
                foreach (var item in array)
                {
                    if (_document.Resolve(item) is PdfDictionary annotation)
                    {
                        annotations.Add(new PdfAnnotation(annotation, (item as PdfReference)?.Id, _document));
                    }
                }
            }

            return annotations;
        }
    }

    /// <summary>The page's links, in the order of its annotations; hidden ones left out.</summary>
    public IReadOnlyList<PdfLink> Links
    {
        get
        {
            var links = new List<PdfLink>();
            foreach (var annotation in Annotations)
            {
                if (annotation.Subtype != PdfAnnotationTypes.Link || annotation.IsHidden)
                {
                    continue;
                }

                var quads = new List<PdfQuad>();
                if (PdfValues.Numbers(annotation.Dictionary["QuadPoints"], _document) is { } points)
                {
                    for (var i = 0; i + 8 <= points.Length; i += 8)
                    {
                        quads.Add(new PdfQuad(new PdfPoint(points[i + 4], points[i + 5]),
                            new PdfPoint(points[i + 6], points[i + 7]), new PdfPoint(points[i + 2], points[i + 3]),
                            new PdfPoint(points[i], points[i + 1])));
                    }
                }

                var dictionary = annotation.Dictionary;
                var action = dictionary[PdfKeys.Dest] != null
                    ? _document.ResolveDestination(dictionary[PdfKeys.Dest]) is { } destination
                        ? PdfAction.ForDestination(destination)
                        : null
                    : PdfAction.From(dictionary["A"], _document);
                links.Add(new PdfLink(annotation.Rect, quads, action));
            }

            return links;
        }
    }

    /// <summary>Reads the page's text: its glyphs with their boxes, gathered into lines - what selection, copying and
    /// search work on. Reads the content each time it is called.</summary>
    public PdfPageText ReadText() => new(PdfTextReader.Read(this, _document));

    /// <summary>The regions of the page with a scale of their own (<c>/VP</c>), in their order.</summary>
    public IReadOnlyList<PdfViewport> Viewports => PdfViewport.ListFrom(Dictionary[PdfKeys.Viewports], _document);

    /// <summary>The content streams decoded and joined, a line break between pieces so that a token cut at a seam
    /// stays two tokens. A piece that cannot be decoded is left out; one past the decode limit throws
    /// <see cref="Filters.PdfDecodeLimitException"/>.</summary>
    public ReadOnlyMemory<byte> GetContents()
    {
        var parts = new List<ReadOnlyMemory<byte>>();
        var length = 0L;
        foreach (var stream in ContentStreams)
        {
            ReadOnlyMemory<byte> data;
            try
            {
                data = _document.Decode(stream).Data;
            }
            catch (Filters.PdfDecodeLimitException)
            {
                throw;
            }
            catch (PdfException)
            {
                continue;
            }

            parts.Add(data);
            length += data.Length + 1;
            if (length > _document.DecodeLimits.MaxDecodedLength + 1L)
            {
                throw new Filters.PdfDecodeLimitException(_document.DecodeLimits.MaxDecodedLength);
            }
        }

        if (parts.Count == 1)
        {
            return parts[0];
        }

        var contents = new byte[Math.Max(0, length - 1)];
        var at = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0)
            {
                contents[at++] = (byte)'\n';
            }

            parts[i].Span.CopyTo(contents.AsSpan(at));
            at += parts[i].Length;
        }

        return contents;
    }

    private static PdfRectangle Clip(PdfRectangle? box, PdfRectangle within)
    {
        if (box is not { } given)
        {
            return within;
        }

        var clipped = given.Intersect(within);
        return clipped.IsEmpty ? within : clipped;
    }

    private static int NormalizeRotation(PdfObject value)
    {
        var degrees = value switch
        {
            PdfInteger integer => integer.Value,
            PdfReal real when double.IsFinite(real.Value) => (long)Math.Round(real.Value),
            _ => 0,
        };
        var quarter = (long)Math.Round(degrees / 90.0);
        return (int)(((quarter % 4) + 4) % 4) * 90;
    }
}
