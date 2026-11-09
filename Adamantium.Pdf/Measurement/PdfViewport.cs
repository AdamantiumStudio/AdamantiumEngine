using System.Collections.Generic;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Measurement;

/// <summary>A region of a page with a scale of its own (ISO 32000-2, 12.9.1, viewport dictionary) - a drawing's
/// plan, its detail - as a page's <c>/VP</c> lists them.</summary>
public sealed class PdfViewport
{
    private const string Type = "Viewport";

    public PdfViewport(PdfRectangle bounds, PdfMeasure measure)
    {
        Bounds = bounds;
        Measure = measure;
    }

    /// <summary>The region, in user space (<c>BBox</c>).</summary>
    public PdfRectangle Bounds { get; }

    /// <summary>Its scale (<c>Measure</c>); null for a viewport with none, or of a kind not read.</summary>
    public PdfMeasure Measure { get; }

    /// <summary>Its name (<c>Name</c>), or null.</summary>
    public string Name { get; init; }

    /// <summary>The viewports of the array <paramref name="value"/>, in its order; those that cannot be read left out.
    /// </summary>
    public static IReadOnlyList<PdfViewport> ListFrom(PdfObject value, IPdfObjectResolver resolver)
    {
        var viewports = new List<PdfViewport>();
        if (resolver.Resolve(value) is not PdfArray array)
        {
            return viewports;
        }

        foreach (var item in array)
        {
            if (resolver.Resolve(item) is PdfDictionary dictionary &&
                PdfRectangle.From(dictionary[PdfKeys.BBox], resolver) is { } bounds)
            {
                viewports.Add(new PdfViewport(bounds, PdfMeasure.From(dictionary[PdfKeys.Measure], resolver))
                {
                    Name = (resolver.Resolve(dictionary["Name"]) as PdfString)?.ToText(),
                });
            }
        }

        return viewports;
    }

    /// <summary>The viewport of <paramref name="viewports"/> that holds (<paramref name="x"/>, <paramref name="y"/>):
    /// the last one that does, a later viewport lying over an earlier one; null when none does.</summary>
    public static PdfViewport At(IReadOnlyList<PdfViewport> viewports, double x, double y)
    {
        for (var i = (viewports?.Count ?? 0) - 1; i >= 0; i--)
        {
            var bounds = viewports[i].Bounds;
            if (x >= bounds.Left && x <= bounds.Right && y >= bounds.Bottom && y <= bounds.Top)
            {
                return viewports[i];
            }
        }

        return null;
    }

    /// <summary>The viewport dictionary.</summary>
    public PdfDictionary ToDictionary()
    {
        var dictionary = new PdfDictionary();
        dictionary.Set(PdfKeys.Type, new PdfName(Type));
        dictionary.Set(PdfKeys.BBox, new PdfArray([new PdfReal(Bounds.Left), new PdfReal(Bounds.Bottom),
            new PdfReal(Bounds.Right), new PdfReal(Bounds.Top)]));
        if (Name != null)
        {
            dictionary.Set("Name", PdfString.FromText(Name));
        }

        if (Measure != null)
        {
            dictionary.Set(PdfKeys.Measure, Measure.ToDictionary());
        }

        return dictionary;
    }
}
