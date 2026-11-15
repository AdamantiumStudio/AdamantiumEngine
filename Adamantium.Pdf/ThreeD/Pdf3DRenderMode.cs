using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.ThreeD;

/// <summary>How a 3D view draws the artwork (ISO 32000-1, 13.6.4, table 306, <c>RM</c>): solid, wireframe,
/// transparent, illustration and the rest, with the colors and opacity some of them use.</summary>
public sealed class Pdf3DRenderMode
{
    private const string SubtypeKey = "Subtype";
    private const string AuxiliaryColorKey = "AC";
    private const string FaceColorKey = "FC";
    private const string OpacityKey = "O";
    private const string CreaseKey = "CV";

    /// <summary>The mode's name (<c>Subtype</c>): <c>Solid</c>, <c>SolidWireframe</c>, <c>Transparent</c>,
    /// <c>Wireframe</c>, <c>Illustration</c>, <c>ShadedVertices</c> and the others the format names.</summary>
    public string Name { get; init; }

    /// <summary>The color of the lines and points the mode adds (<c>AC</c>); black by default.</summary>
    public Pdf3DColor AuxiliaryColor { get; init; } = new(0, 0, 0);

    /// <summary>The faces' color for the modes that paint them flat (<c>FC</c>); null for the view's background.
    /// </summary>
    public Pdf3DColor FaceColor { get; init; }

    /// <summary>How opaque the transparent modes draw the faces (<c>O</c>), 0 to 1; 0.5 by default.</summary>
    public double Opacity { get; init; } = 0.5;

    /// <summary>The angle between faces, in degrees, from which their edge counts as a crease (<c>CV</c>); 45 by
    /// default.</summary>
    public double CreaseAngle { get; init; } = 45;

    /// <summary>The mode of an <c>RM</c> dictionary, or null for none - the artwork's own.</summary>
    public static Pdf3DRenderMode From(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is not PdfDictionary dictionary ||
            PdfValues.Name(dictionary[SubtypeKey], resolver) is not { } name)
        {
            return null;
        }

        return new Pdf3DRenderMode
        {
            Name = name,
            AuxiliaryColor = Pdf3DColors.From(dictionary[AuxiliaryColorKey], resolver) ?? new Pdf3DColor(0, 0, 0),
            FaceColor = Pdf3DColors.From(dictionary[FaceColorKey], resolver),
            Opacity = PdfValues.Number(dictionary[OpacityKey], resolver, 0.5),
            CreaseAngle = PdfValues.Number(dictionary[CreaseKey], resolver, 45),
        };
    }
}
