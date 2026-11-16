using System;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.ThreeD;

/// <summary>What ties a markup annotation to a view of 3D artwork (ISO 32000-1, 13.6.6, table 313, an <c>ExData</c>
/// dictionary of subtype <c>Markup3D</c>): the 3D annotation it comments on, the view it is shown in - only while that
/// very view is the annotation's current one - and the checksum of the 3D stream it was made on.</summary>
public sealed class PdfMarkup3D
{
    private const string TypeKey = "Type";
    private const string SubtypeKey = "Subtype";
    private const string AnnotationKey = "3DA";
    private const string ViewKey = "3DV";
    private const string ChecksumKey = "MD5";
    private const string ExternalDataType = "ExData";
    private const string Markup3DSubtype = "Markup3D";

    /// <summary>The object of the 3D annotation commented on (<c>3DA</c> as a reference), or null.</summary>
    public PdfObjectId? Annotation { get; init; }

    /// <summary>The name (<c>NM</c>) of the 3D annotation commented on, on the same page (<c>3DA</c> as a text string), or
    /// null.</summary>
    public string AnnotationName { get; init; }

    /// <summary>The view the markup is shown in (<c>3DV</c>).</summary>
    public Pdf3DView View { get; init; }

    /// <summary>The MD5 checksum of the 3D stream's data the markup was made on (<c>MD5</c>), sixteen bytes, or null.
    /// </summary>
    public byte[] Checksum { get; init; }

    /// <summary>The markup of an <c>ExData</c> dictionary, or null for none, another kind of external data, or one that
    /// names no view.</summary>
    public static PdfMarkup3D From(PdfObject value, IPdfObjectResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (resolver.Resolve(value) is not PdfDictionary dictionary ||
            PdfValues.Name(dictionary[SubtypeKey], resolver) != Markup3DSubtype ||
            Pdf3DView.From(dictionary[ViewKey], resolver) is not { } view)
        {
            return null;
        }

        var annotation = dictionary[AnnotationKey];
        return new PdfMarkup3D
        {
            Annotation = (annotation as PdfReference)?.Id,
            AnnotationName = (resolver.Resolve(annotation) as PdfString)?.ToText(),
            View = view,
            Checksum = (resolver.Resolve(dictionary[ChecksumKey]) as PdfString)?.Bytes,
        };
    }

    /// <summary>The <c>ExData</c> dictionary of a markup on <paramref name="view"/> - a reference to a view object, or a
    /// view dictionary - of the 3D annotation <paramref name="annotation"/>, made on the stream of
    /// <paramref name="checksum"/> (null to leave it out).</summary>
    public static PdfDictionary ToDictionary(PdfReference annotation, PdfObject view, byte[] checksum)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(view);
        var dictionary = new PdfDictionary();
        dictionary.Set(TypeKey, new PdfName(ExternalDataType));
        dictionary.Set(SubtypeKey, new PdfName(Markup3DSubtype));
        dictionary.Set(AnnotationKey, annotation);
        dictionary.Set(ViewKey, view);
        if (checksum is { Length: > 0 })
        {
            dictionary.Set(ChecksumKey, new PdfString(checksum, isHex: true));
        }

        return dictionary;
    }
}
