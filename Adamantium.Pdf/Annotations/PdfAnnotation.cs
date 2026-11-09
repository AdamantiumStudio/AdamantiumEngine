using Adamantium.Pdf.Document;
using Adamantium.Pdf.Measurement;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Annotations;

/// <summary>An annotation of a page as the file has it (ISO 32000-2, 12.5): its subtype, rectangle and the entries
/// every kind shares; the dictionary for the rest. An annotation a developer extension wrote carries that extension's
/// data under its own key (<see cref="ExtensionData"/>).</summary>
public sealed class PdfAnnotation
{
    private readonly IPdfObjectResolver _resolver;

    internal PdfAnnotation(PdfDictionary dictionary, PdfObjectId? id, IPdfObjectResolver resolver)
    {
        _resolver = resolver;
        Dictionary = dictionary;
        Id = id;
        Subtype = (resolver.Resolve(dictionary[PdfKeys.Subtype]) as PdfName)?.Value ?? "";
        Rect = PdfRectangle.From(dictionary[PdfAnnotationKeys.Rect], resolver) ?? default;
        Flags = PdfValues.Integer(dictionary[PdfAnnotationKeys.Flags], resolver);
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

    /// <summary>Its name, unique on the page (<c>NM</c>), or null.</summary>
    public string Name => Text(PdfAnnotationKeys.Name);

    /// <summary>Its text, or what it says in words (<c>Contents</c>), or null.</summary>
    public string Contents => Text(PdfAnnotationKeys.Contents);

    /// <summary>Who made it (<c>T</c>), or null.</summary>
    public string Author => Text(PdfAnnotationKeys.Title);

    /// <summary>The scale of a measurement annotation (<c>Measure</c>), or null.</summary>
    public PdfMeasure Measure => PdfMeasure.From(Dictionary[PdfKeys.Measure], _resolver);

    /// <summary>The dictionary a developer extension keeps under <paramref name="key"/> - a name with the
    /// extension's prefix (ISO 32000-2, annex E) - or null when it has none there.</summary>
    public PdfDictionary ExtensionData(string key) => _resolver.Resolve(Dictionary[key]) as PdfDictionary;

    private string Text(string key) => (_resolver.Resolve(Dictionary[key]) as PdfString)?.ToText();
}
