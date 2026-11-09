using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Resources;

/// <summary>A resource dictionary (ISO 32000-2, 7.8.3): what a content stream names by key - fonts, external objects,
/// graphics states, color spaces, patterns, shadings, marked-content properties.</summary>
public sealed class PdfResources
{
    private readonly IPdfObjectResolver _resolver;

    public PdfResources(PdfDictionary dictionary, IPdfObjectResolver resolver)
    {
        Dictionary = dictionary ?? new PdfDictionary();
        _resolver = resolver;
    }

    public PdfDictionary Dictionary { get; }

    public IPdfObjectResolver Resolver => _resolver;

    public PdfDictionary Font(string name) => Get(PdfKeys.Font, name) as PdfDictionary;

    /// <summary>An image or form, which are streams.</summary>
    public PdfStream XObject(string name) => Get(PdfKeys.XObject, name) as PdfStream;

    public PdfDictionary ExtGState(string name) => Get(PdfKeys.ExtGState, name) as PdfDictionary;

    /// <summary>A named color space as written: a name or an array.</summary>
    public PdfObject ColorSpace(string name) => Get(PdfKeys.ColorSpace, name);

    /// <summary>A pattern: a stream (tiling) or a dictionary (shading).</summary>
    public PdfObject Pattern(string name) => Get(PdfKeys.Pattern, name);

    /// <summary>A shading: a dictionary, or a stream for mesh shadings.</summary>
    public PdfObject Shading(string name) => Get(PdfKeys.Shading, name);

    public PdfDictionary Properties(string name) => Get(PdfKeys.Properties, name) as PdfDictionary;

    /// <summary>The entry <paramref name="name"/> of <paramref name="category"/> as written - a reference stays one, so
    /// that what is read through it once can be kept by the document.</summary>
    public PdfObject Entry(string category, string name) =>
        (_resolver.Resolve(Dictionary[category]) as PdfDictionary)?[name];

    /// <summary>The resource <paramref name="name"/> of <paramref name="category"/>, resolved, or null.</summary>
    public PdfObject Get(string category, string name)
    {
        if (_resolver.Resolve(Dictionary[category]) is not PdfDictionary entries)
        {
            return null;
        }

        var value = _resolver.Resolve(entries[name]);
        return value is PdfNull ? null : value;
    }
}
