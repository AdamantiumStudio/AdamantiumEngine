using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.ThreeD;

/// <summary>How a 3D view projects the artwork onto the annotation (ISO 32000-1, 13.6.4, table 304, <c>P</c>).</summary>
public sealed class Pdf3DProjection
{
    private const string Subtype = "Subtype";
    private const string FieldOfViewKey = "FOV";
    private const string PerspectiveScalingKey = "PS";
    private const string OrthographicScaleKey = "OS";
    private const string OrthographicBindingKey = "OB";
    private const string Orthographic = "O";
    private const string Perspective = "P";
    private const string NearKey = "N";
    private const double NearDistance = 1;
    private const string WidthBinding = "W";
    private const string AbsoluteBinding = "Absolute";

    /// <summary>Perspective, or orthographic (<c>Subtype</c> <c>P</c> or <c>O</c>).</summary>
    public Pdf3DProjectionKind Kind { get; init; }

    /// <summary>The perspective's full field of view, in degrees, 0 to 180 (<c>FOV</c>).</summary>
    public double FieldOfView { get; init; }

    /// <summary>The orthographic projection's scale (<c>OS</c>), applied to the near plane's x and y on top of its
    /// binding's; 1 by default.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>How the near plane of an orthographic projection is fitted to the annotation (<c>OB</c>): to its width
    /// <c>W</c>, its height <c>H</c>, the lesser <c>Min</c> or greater <c>Max</c> of the two, or not at all,
    /// <c>Absolute</c> - one unit of the artwork a unit of the annotation - the default; for a perspective, the side
    /// its field of view spans (<c>PS</c>), <c>W</c> by default.</summary>
    public string Binding { get; init; }

    /// <summary>The <c>P</c> dictionary of the projection; a perspective one with the near distance the format requires
    /// (<c>N</c>), which clips nothing while the clipping is automatic, as it is by default.</summary>
    public PdfDictionary ToDictionary()
    {
        var dictionary = new PdfDictionary();
        var orthographic = Kind == Pdf3DProjectionKind.Orthographic;
        dictionary.Set(Subtype, new PdfName(orthographic ? Orthographic : Perspective));
        if (orthographic)
        {
            dictionary.Set(OrthographicScaleKey, new PdfReal(Scale));
            dictionary.Set(OrthographicBindingKey, new PdfName(Binding ?? AbsoluteBinding));
        }
        else
        {
            dictionary.Set(NearKey, new PdfReal(NearDistance));
            dictionary.Set(FieldOfViewKey, new PdfReal(FieldOfView));
            dictionary.Set(PerspectiveScalingKey, new PdfName(Binding ?? WidthBinding));
        }

        return dictionary;
    }

    /// <summary>The projection of a <c>P</c> dictionary; perspective with a 90 degree field of view, as the format
    /// takes for none.</summary>
    public static Pdf3DProjection From(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is not PdfDictionary dictionary)
        {
            return new Pdf3DProjection { Kind = Pdf3DProjectionKind.Perspective, FieldOfView = 90 };
        }

        var orthographic = PdfValues.Name(dictionary[Subtype], resolver) == Orthographic;
        return new Pdf3DProjection
        {
            Kind = orthographic ? Pdf3DProjectionKind.Orthographic : Pdf3DProjectionKind.Perspective,
            FieldOfView = PdfValues.Number(dictionary[FieldOfViewKey], resolver, 90),
            Scale = PdfValues.Number(dictionary[OrthographicScaleKey], resolver, 1),
            Binding = orthographic
                ? PdfValues.Name(dictionary[OrthographicBindingKey], resolver) ?? AbsoluteBinding
                : PdfValues.Name(dictionary[PerspectiveScalingKey], resolver) ?? WidthBinding,
        };
    }
}
