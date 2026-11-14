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

    /// <summary>Perspective, or orthographic (<c>Subtype</c> <c>P</c> or <c>O</c>).</summary>
    public Pdf3DProjectionKind Kind { get; init; }

    /// <summary>The perspective's full field of view, in degrees, 0 to 180 (<c>FOV</c>).</summary>
    public double FieldOfView { get; init; }

    /// <summary>The orthographic projection's scale (<c>OS</c>): world units to the binding's length; 1 by default.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>What an orthographic scale is a fraction of (<c>OB</c>): <c>W</c> width, <c>H</c> height, <c>Min</c> or
    /// <c>Max</c> of the two, <c>Absolute</c>; or what a perspective's field of view spans (<c>PS</c>).</summary>
    public string Binding { get; init; }

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
                ? PdfValues.Name(dictionary[OrthographicBindingKey], resolver) ?? "W"
                : PdfValues.Name(dictionary[PerspectiveScalingKey], resolver) ?? "W",
        };
    }
}
