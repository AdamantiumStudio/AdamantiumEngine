using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Measurement;
using Adamantium.Pdf.Navigation;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Annotations;

/// <summary>An annotation to write into a page (<see cref="Writing.PdfIncrementalUpdate.AddAnnotation"/>): its
/// subtype and rectangle, the entries its kind has, the appearance any reader shows it by, and the data of a
/// developer extension under the extension's own key, which readers that do not know it leave alone.</summary>
public sealed class PdfNewAnnotation
{
    public PdfNewAnnotation(string subtype, PdfRectangle rect)
    {
        ArgumentException.ThrowIfNullOrEmpty(subtype);
        Subtype = subtype;
        Rect = rect;
    }

    public string Subtype { get; }

    /// <summary>Where it is on the page, in default user space.</summary>
    public PdfRectangle Rect { get; }

    /// <summary>Its flags; printed (4) unless set otherwise.</summary>
    public int Flags { get; init; } = 4;

    /// <summary>Its name, unique on the page (<c>NM</c>).</summary>
    public string Name { get; init; }

    public string Contents { get; init; }

    public string Author { get; init; }

    /// <summary>What it is about in a few words (<c>Subj</c>); a viewer's list of comments heads it with this.</summary>
    public string Subject { get; init; }

    /// <summary>When it was made (<c>CreationDate</c>).</summary>
    public DateTimeOffset? Created { get; init; }

    /// <summary>When it was last changed (<c>M</c>).</summary>
    public DateTimeOffset? Modified { get; init; }

    /// <summary>Its color, DeviceRGB components 0..1 (<c>C</c>); null for none.</summary>
    public IReadOnlyList<double> Color { get; init; }

    /// <summary>The color a closed shape is filled with (<c>IC</c>); null for none.</summary>
    public IReadOnlyList<double> InteriorColor { get; init; }

    /// <summary>Its opacity, 0..1 (<c>CA</c>).</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>The width of its border or line, in points (<c>BS</c> <c>W</c>).</summary>
    public double BorderWidth { get; init; } = 1;

    /// <summary>Its dashes, on and off in points (<c>BS</c> <c>D</c>); null or empty for solid.</summary>
    public IReadOnlyList<double> Dash { get; init; }

    /// <summary>A line's ends (<c>L</c>): x1, y1, x2, y2.</summary>
    public IReadOnlyList<double> Line { get; init; }

    /// <summary>How a line's or a polyline's ends are drawn (<c>LE</c>), as "None", "OpenArrow", "ClosedArrow".
    /// </summary>
    public (string Start, string End)? LineEndings { get; init; }

    /// <summary>A polygon's or a polyline's vertices (<c>Vertices</c>): x, y pairs.</summary>
    public IReadOnlyList<double> Vertices { get; init; }

    /// <summary>An ink annotation's strokes (<c>InkList</c>), each x, y pairs.</summary>
    public IReadOnlyList<IReadOnlyList<double>> InkList { get; init; }

    /// <summary>The quadrilaterals of a text markup (<c>QuadPoints</c>), eight numbers each.</summary>
    public IReadOnlyList<double> QuadPoints { get; init; }

    /// <summary>A free text annotation's default appearance (<c>DA</c>); a black Helvetica of 12 points unless set.
    /// </summary>
    public string DefaultAppearance { get; init; }

    /// <summary>What the annotation is for (<c>IT</c>), as "LineDimension", "PolygonDimension".</summary>
    public string Intent { get; init; }

    /// <summary>The scale of a measurement (<c>Measure</c>).</summary>
    public PdfMeasure Measure { get; init; }

    /// <summary>How it looks (<c>AP</c> <c>N</c>): a form XObject, as <see cref="Writing.PdfContentBuilder"/> makes.
    /// </summary>
    public PdfStream Appearance { get; init; }

    /// <summary>The key of a developer extension's data (<see cref="ExtensionData"/>): a name with the extension's
    /// prefix and an underscore (ISO 32000-2, annex E).</summary>
    public string ExtensionKey { get; init; }

    /// <summary>The extension's data, kept under <see cref="ExtensionKey"/>.</summary>
    public PdfDictionary ExtensionData { get; init; }

    /// <summary>The annotation dictionary, of the page <paramref name="page"/>, its appearance as
    /// <paramref name="appearance"/>.</summary>
    internal PdfDictionary ToDictionary(PdfReference page, PdfReference appearance)
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Type, new PdfName(PdfAnnotationKeys.Annot));
        dictionary.Set(PdfKeys.Subtype, new PdfName(Subtype));
        dictionary.Set(PdfAnnotationKeys.Rect, Numbers([Rect.Left, Rect.Bottom, Rect.Right, Rect.Top]));
        if (page != null)
        {
            dictionary.Set(PdfAnnotationKeys.Page, page);
        }

        dictionary.Set(PdfAnnotationKeys.Flags, new PdfInteger(Flags));
        SetText(dictionary, PdfAnnotationKeys.Name, Name);
        SetText(dictionary, PdfAnnotationKeys.Contents, Contents);
        SetText(dictionary, PdfAnnotationKeys.Title, Author);
        SetText(dictionary, PdfAnnotationKeys.Subject, Subject);
        if (Created is { } created)
        {
            dictionary.Set(PdfAnnotationKeys.CreationDate, PdfString.FromText(PdfDate.Format(created)));
        }

        if (Modified is { } modified)
        {
            dictionary.Set(PdfAnnotationKeys.Modified, PdfString.FromText(PdfDate.Format(modified)));
        }

        if (Color != null)
        {
            dictionary.Set(PdfAnnotationKeys.Color, Numbers(Color));
        }

        if (InteriorColor != null)
        {
            dictionary.Set(PdfAnnotationKeys.InteriorColor, Numbers(InteriorColor));
        }

        if (Opacity < 1)
        {
            dictionary.Set(PdfAnnotationKeys.Opacity, new PdfReal(Math.Clamp(Opacity, 0, 1)));
        }

        var border = new PdfDictionary();
        border.Set(PdfAnnotationKeys.BorderWidth, new PdfReal(Math.Max(0, BorderWidth)));
        if (Dash is { Count: > 0 })
        {
            border.Set(PdfAnnotationKeys.BorderStyle, new PdfName("D"));
            border.Set(PdfAnnotationKeys.Dash, Numbers(Dash));
        }

        dictionary.Set(PdfAnnotationKeys.Border, border);
        if (Line is { Count: 4 })
        {
            dictionary.Set(PdfAnnotationKeys.Line, Numbers(Line));
        }

        if (LineEndings is { } endings)
        {
            dictionary.Set(PdfAnnotationKeys.LineEndings,
                new PdfArray([new PdfName(endings.Start ?? "None"), new PdfName(endings.End ?? "None")]));
        }

        if (Vertices != null)
        {
            dictionary.Set(PdfAnnotationKeys.Vertices, Numbers(Vertices));
        }

        if (InkList != null)
        {
            dictionary.Set(PdfAnnotationKeys.InkList,
                new PdfArray(InkList.Select(stroke => (PdfObject)Numbers(stroke)).ToList()));
        }

        if (QuadPoints != null)
        {
            dictionary.Set(PdfAnnotationKeys.QuadPoints, Numbers(QuadPoints));
        }

        if (Subtype == PdfAnnotationTypes.FreeText || DefaultAppearance != null)
        {
            SetText(dictionary, PdfAnnotationKeys.DefaultAppearance, DefaultAppearance ?? "0 0 0 rg /Helv 12 Tf");
        }

        if (Intent != null)
        {
            dictionary.Set(PdfAnnotationKeys.Intent, new PdfName(Intent));
        }

        if (Measure != null)
        {
            dictionary.Set(PdfKeys.Measure, Measure.ToDictionary());
        }

        if (appearance != null)
        {
            var appearances = new PdfDictionary();
            appearances.Set(PdfAnnotationKeys.Normal, appearance);
            dictionary.Set(PdfAnnotationKeys.Appearance, appearances);
        }

        if (ExtensionKey != null && ExtensionData != null)
        {
            dictionary.Set(ExtensionKey, ExtensionData);
        }

        return dictionary;
    }

    private static void SetText(PdfDictionary dictionary, string key, string value)
    {
        if (value != null)
        {
            dictionary.Set(key, PdfString.FromText(value));
        }
    }

    private static PdfArray Numbers(IEnumerable<double> values) =>
        new(values.Select(value => (PdfObject)new PdfReal(value)).ToList());
}
