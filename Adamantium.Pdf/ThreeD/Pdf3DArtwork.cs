using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.ThreeD;

/// <summary>The 3D artwork of a 3D annotation (ISO 32000-1, 13.6): the model's stream and format, its views, activation
/// and box; the model itself comes out as bytes (<see cref="ReadData"/>).</summary>
public sealed class Pdf3DArtwork
{
    private const string ArtworkKey = "3DD";
    private const string DefaultViewKey = "3DV";
    private const string ActivationKey = "3DA";
    private const string ViewBoxKey = "3DB";
    private const string InteractiveKey = "3DI";
    private const string ViewsKey = "VA";
    private const string StreamDefaultViewKey = "DV";
    private const string TypeKey = "Type";
    private const string SubtypeKey = "Subtype";
    private const string ReferenceType = "3DRef";
    private const string FirstView = "F";
    private const string LastView = "L";
    private const string StreamsDefault = "D";

    private readonly PdfDocument _document;

    private Pdf3DArtwork(PdfDocument document, PdfStream stream)
    {
        _document = document;
        Stream = stream;
    }

    /// <summary>The model's stream (<c>3DD</c>, through a <c>3DRef</c> when another annotation holds it).</summary>
    public PdfStream Stream { get; }

    /// <summary>The model's format (<c>Subtype</c>): <c>U3D</c> (ECMA-363) or <c>PRC</c> (ISO 14739-1).</summary>
    public string Format { get; private init; }

    /// <summary>Whether the stream is another annotation's, named by a <c>3DRef</c>: one model, several views of it.
    /// </summary>
    public bool IsShared { get; private init; }

    /// <summary>The views the stream lists (<c>VA</c>), in its order.</summary>
    public IReadOnlyList<Pdf3DView> Views { get; private init; }

    /// <summary>The view it opens on (<c>3DV</c> of the annotation, else <c>DV</c> of the stream, else the first of
    /// <see cref="Views"/>); null for none - the model's own camera.</summary>
    public Pdf3DView DefaultView { get; private init; }

    /// <summary>When it comes to life and goes back to its poster (<c>3DA</c>).</summary>
    public Pdf3DActivation Activation { get; private init; }

    /// <summary>The part of the annotation it is shown in (<c>3DB</c>), in the annotation's target space: points from the
    /// middle of its rectangle, so the whole rectangle is [-w/2 -h/2 w/2 h/2]; null for the whole annotation.</summary>
    public PdfRectangle? ViewBox { get; private init; }

    /// <summary>Whether the reader may turn and move it (<c>3DI</c>); true by default.</summary>
    public bool IsInteractive { get; private init; }

    /// <summary>The model's file, decoded from the stream's filters.</summary>
    public ReadOnlyMemory<byte> ReadData() => _document.Decode(Stream).Data;

    /// <summary>The MD5 checksum of the stream's data as the file holds it - what a 3D comment keeps (<c>MD5</c>,
    /// 13.6.6) to tell whether the model changed since it was made.</summary>
    public byte[] ComputeChecksum() => MD5.HashData(Stream.Data.Span);

    /// <summary>The artwork of a 3D annotation's dictionary, or null when it names no 3D stream.</summary>
    public static Pdf3DArtwork From(PdfDictionary annotation, PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(document);
        var named = document.Resolve(annotation[ArtworkKey]);
        var shared = false;
        var stream = named as PdfStream;
        if (named is PdfDictionary reference && PdfValues.Name(reference[TypeKey], document) == ReferenceType)
        {
            shared = true;
            stream = document.Resolve(reference[ArtworkKey]) as PdfStream;
        }

        if (stream == null)
        {
            return null;
        }

        List<Pdf3DView> views = [];
        var viewOfDictionary = new Dictionary<PdfDictionary, Pdf3DView>(ReferenceEqualityComparer.Instance);
        if (document.Resolve(stream.Dictionary[ViewsKey]) is PdfArray listed)
        {
            foreach (var item in listed)
            {
                if (Pdf3DView.From(item, document) is { } view)
                {
                    views.Add(view);
                    viewOfDictionary.TryAdd((PdfDictionary)document.Resolve(item), view);
                }
            }
        }

        var streamDefault = PickView(stream.Dictionary[StreamDefaultViewKey], views, viewOfDictionary, null, document);
        return new Pdf3DArtwork(document, stream)
        {
            Format = PdfValues.Name(stream.Dictionary[SubtypeKey], document),
            IsShared = shared,
            Views = views,
            DefaultView = PickView(annotation[DefaultViewKey], views, viewOfDictionary, streamDefault, document) ?? streamDefault ??
                          (views.Count > 0 ? views[0] : null),
            Activation = Pdf3DActivation.From(annotation[ActivationKey], document),
            ViewBox = PdfRectangle.From(annotation[ViewBoxKey], document),
            IsInteractive = PdfValues.Boolean(annotation[InteractiveKey], document, true),
        };
    }

    private static Pdf3DView PickView(PdfObject value, List<Pdf3DView> views, Dictionary<PdfDictionary, Pdf3DView> viewOfDictionary,
        Pdf3DView streamDefault, IPdfObjectResolver resolver)
    {
        switch (resolver.Resolve(value))
        {
            case PdfInteger index:
                return index.Value >= 0 && index.Value < views.Count ? views[(int)index.Value] : null;
            case PdfName { Value: FirstView }:
                return views.Count > 0 ? views[0] : null;
            case PdfName { Value: LastView }:
                return views.Count > 0 ? views[^1] : null;
            case PdfName { Value: StreamsDefault }:
                return streamDefault;
            case PdfString name:
                var wanted = name.ToText();
                return views.Find(view => view.Name == wanted || view.InternalName == wanted);
            case PdfDictionary dictionary:
                return viewOfDictionary.TryGetValue(dictionary, out var listedView) ? listedView : Pdf3DView.From(value, resolver);
            default:
                return null;
        }
    }
}
