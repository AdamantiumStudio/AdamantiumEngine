using System;
using System.Collections.Generic;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Measurement;
using Adamantium.Pdf.Navigation;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.View3D;

namespace Adamantium.Pdf.Annotations;

/// <summary>An annotation of a page as the file has it (ISO 32000-2, 12.5): its subtype, rectangle and the entries
/// every kind shares; the dictionary for the rest. An annotation is shown by its appearance (<see cref="Appearance"/>)
/// set into its rectangle (<see cref="AppearanceTransform"/>). An annotation a developer extension wrote carries that
/// extension's data under its own key (<see cref="ExtensionData"/>).</summary>
public sealed class PdfAnnotation
{
    private readonly PdfDocument _document;

    internal PdfAnnotation(PdfDictionary dictionary, PdfObjectId? id, PdfDocument document)
    {
        _document = document;
        Dictionary = dictionary;
        Id = id;
        Subtype = (document.Resolve(dictionary[PdfKeys.Subtype]) as PdfName)?.Value ?? "";
        Rect = PdfRectangle.From(dictionary[PdfAnnotationKeys.Rect], document) ?? default;
        Flags = PdfValues.Integer(dictionary[PdfAnnotationKeys.Flags], document);
    }

    /// <summary>The annotation's object; null for one written directly into the page's array.</summary>
    public PdfObjectId? Id { get; }

    public PdfDictionary Dictionary { get; }

    /// <summary>Its kind (<c>Subtype</c>), as <see cref="PdfAnnotationTypes"/> names them.</summary>
    public string Subtype { get; }

    /// <summary>Where it is on the page, in default user space (<c>Rect</c>).</summary>
    public PdfRectangle Rect { get; }

    /// <summary>Its flags (<c>F</c>, ISO 32000-2, table 167): 2 hidden, 4 printed, 32 not viewed.</summary>
    public int Flags { get; }

    /// <summary>Whether a viewer leaves it out: hidden, or not to be viewed (flags 2 and 32).</summary>
    public bool IsHidden => (Flags & (2 | 32)) != 0;

    /// <summary>Its name, unique on the page (<c>NM</c>), or null.</summary>
    public string Name => Text(PdfAnnotationKeys.Name);

    /// <summary>Its text, or what it says in words (<c>Contents</c>), or null.</summary>
    public string Contents => Text(PdfAnnotationKeys.Contents);

    /// <summary>Who made it (<c>T</c>), or null.</summary>
    public string Author => Text(PdfAnnotationKeys.Title);

    /// <summary>What it is about in a few words (<c>Subj</c>), or null.</summary>
    public string Subject => Text(PdfAnnotationKeys.Subject);

    /// <summary>When it was made (<c>CreationDate</c>), or null.</summary>
    public DateTimeOffset? Created => PdfDate.Parse(Text(PdfAnnotationKeys.CreationDate));

    /// <summary>When it was last changed (<c>M</c>), or null.</summary>
    public DateTimeOffset? Modified => PdfDate.Parse(Text(PdfAnnotationKeys.Modified));

    /// <summary>The scale of a measurement annotation (<c>Measure</c>), or null.</summary>
    public PdfMeasure Measure => PdfMeasure.From(Dictionary[PdfKeys.Measure], _document);

    /// <summary>The file a file attachment annotation holds (<c>FS</c>), or null.</summary>
    public PdfAttachment Attachment => PdfAttachment.From(null, Dictionary["FS"], _document);

    /// <summary>The 3D artwork of a 3D annotation (<see cref="PdfAnnotationTypes.ThreeD"/>), or null for another kind or
    /// one that names no 3D stream.</summary>
    public Pdf3DArtwork Artwork3D =>
        Subtype == PdfAnnotationTypes.ThreeD ? Pdf3DArtwork.From(Dictionary, _document) : null;

    /// <summary>The view of 3D artwork a markup annotation comments on (<c>ExData</c> of subtype <c>Markup3D</c>,
    /// ISO 32000-1, 13.6.6), or null for one that comments on none.</summary>
    public PdfMarkup3D Markup3D => PdfMarkup3D.From(Dictionary[PdfAnnotationKeys.ExternalData], _document);

    /// <summary>Its border (<c>BS</c>, else <c>Border</c>; 1 point solid when it gives neither).</summary>
    public PdfAnnotationBorder Border => PdfAnnotationBorder.From(Dictionary, _document);

    /// <summary>Its color (<c>C</c>): 1 number gray, 3 RGB, 4 CMYK, none transparent; null when it gives none.</summary>
    public IReadOnlyList<double> Color => PdfValues.Numbers(Dictionary[PdfAnnotationKeys.Color], _document);

    /// <summary>What a viewer draws for it when the file gives it no appearance (no <c>AP</c>) and its entries say how
    /// it looks: a link's border round its rectangle, or round each of its quadrilaterals, in its color (black when it
    /// names none). Null otherwise - for a link with a border of no width or no color, and for any other kind.</summary>
    public PdfStream ImpliedAppearance => Subtype == PdfAnnotationTypes.Link && Dictionary[PdfAnnotationKeys.Appearance] == null
        ? PdfImpliedAppearance.LinkBorder(Rect, Border, Color,
            PdfValues.Numbers(Dictionary[PdfAnnotationKeys.QuadPoints], _document))
        : null;

    /// <summary>Its normal appearance (<c>AP</c> <c>N</c>) - the one of its state (<c>AS</c>) when it has several;
    /// null when it has none.</summary>
    public PdfStream Appearance => AppearanceOf(PdfAnnotationKeys.Normal);

    /// <summary>How it looks while pressed (<c>AP</c> <c>D</c>) - the one of its state (<c>AS</c>) when it has several;
    /// null when it gives none.</summary>
    public PdfStream DownAppearance => AppearanceOf(PdfAnnotationKeys.Down);

    private PdfStream AppearanceOf(string kind)
    {
        var appearances = _document.Resolve(Dictionary[PdfAnnotationKeys.Appearance]) as PdfDictionary;
        return _document.Resolve(appearances?[kind]) switch
        {
            PdfStream stream => stream,
            PdfDictionary states => _document.Resolve(
                states[PdfValues.Name(Dictionary["AS"], _document) ?? ""]) as PdfStream,
            _ => null,
        };
    }

    /// <summary>What sets <paramref name="appearance"/> - a form of this annotation - into <see cref="Rect"/> on the
    /// page (ISO 32000-2, 12.5.5): its matrix, then the scale and offset that bring its box, so turned, onto the
    /// rectangle.</summary>
    public PdfMatrix AppearanceTransform(PdfStream appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        var matrix = PdfMatrix.From(PdfValues.Numbers(appearance.Dictionary[PdfKeys.Matrix], _document));
        if (PdfRectangle.From(appearance.Dictionary[PdfKeys.BBox], _document) is not { } box)
        {
            return matrix.Then(new PdfMatrix(1, 0, 0, 1, Rect.Left, Rect.Bottom));
        }

        var corners = new[]
        {
            matrix.Transform(box.Left, box.Bottom), matrix.Transform(box.Right, box.Bottom),
            matrix.Transform(box.Right, box.Top), matrix.Transform(box.Left, box.Top),
        };
        double left = double.MaxValue, bottom = double.MaxValue, right = double.MinValue, top = double.MinValue;
        foreach (var corner in corners)
        {
            (left, right) = (Math.Min(left, corner.X), Math.Max(right, corner.X));
            (bottom, top) = (Math.Min(bottom, corner.Y), Math.Max(top, corner.Y));
        }

        var scaleX = right - left > 1e-9 ? Rect.Width / (right - left) : 1;
        var scaleY = top - bottom > 1e-9 ? Rect.Height / (top - bottom) : 1;
        return matrix.Then(new PdfMatrix(scaleX, 0, 0, scaleY, Rect.Left - left * scaleX, Rect.Bottom - bottom * scaleY));
    }

    /// <summary>The dictionary a developer extension keeps under <paramref name="key"/> - a name with the
    /// extension's prefix (ISO 32000-2, annex E) - or null when it has none there.</summary>
    public PdfDictionary ExtensionData(string key) => _document.Resolve(Dictionary[key]) as PdfDictionary;

    private string Text(string key) => (_document.Resolve(Dictionary[key]) as PdfString)?.ToText();
}
