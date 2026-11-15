using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.ThreeD;

/// <summary>A named view of 3D artwork (ISO 32000-1, 13.6.4, table 298): where the camera stands, how it projects,
/// the background, how the artwork is drawn and lit.</summary>
public sealed class Pdf3DView
{
    private const string ExternalNameKey = "XN";
    private const string InternalNameKey = "IN";
    private const string MatrixSourceKey = "MS";
    private const string CameraKey = "C2W";
    private const string ViewNodeKey = "U3DPath";
    private const string CenterOfOrbitKey = "CO";
    private const string ProjectionKey = "P";
    private const string BackgroundKey = "BG";
    private const string BackgroundColorKey = "C";
    private const string RenderModeKey = "RM";
    private const string LightingKey = "LS";
    private const string SubtypeKey = "Subtype";
    private const string MatrixFromCamera = "M";

    /// <summary>The name a person reads (<c>XN</c>), as a viewer's list of views shows it.</summary>
    public string Name { get; init; }

    /// <summary>The name the artwork's scripts know it by (<c>IN</c>), or null.</summary>
    public string InternalName { get; init; }

    /// <summary>Whether the camera is <see cref="Camera"/> (<c>MS</c> <c>M</c>) rather than a view node of the artwork
    /// itself (<c>U3D</c>, named by <see cref="ViewNode"/>); with neither, the artwork's own default camera.</summary>
    public bool UsesCamera { get; init; }

    /// <summary>The camera (<c>C2W</c>), or null.</summary>
    public Pdf3DCamera Camera { get; init; }

    /// <summary>The artwork's view node the camera is taken from (<c>U3DPath</c>), or null.</summary>
    public string ViewNode { get; init; }

    /// <summary>How far in front of the camera, along its Z axis, the point it orbits round lies (<c>CO</c>).</summary>
    public double CenterOfOrbit { get; init; }

    /// <summary>How the view projects (<c>P</c>).</summary>
    public Pdf3DProjection Projection { get; init; }

    /// <summary>The background (<c>BG</c>); white by default.</summary>
    public Pdf3DColor Background { get; init; } = new(1, 1, 1);

    /// <summary>How the artwork is drawn (<c>RM</c>); null for the artwork's own way.</summary>
    public Pdf3DRenderMode RenderMode { get; init; }

    /// <summary>How the artwork is lit (<c>LS</c> <c>Subtype</c>): <c>Artwork</c>, <c>None</c>, <c>White</c>, <c>Day</c>,
    /// <c>Night</c>, <c>Hard</c>, <c>Primary</c>, <c>Blue</c>, <c>Red</c>, <c>Cube</c>, <c>CAD</c>, <c>Headlamp</c>; null
    /// for the artwork's own lights.</summary>
    public string Lighting { get; init; }

    /// <summary>The view of a view dictionary, or null for anything else.</summary>
    public static Pdf3DView From(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is not PdfDictionary dictionary)
        {
            return null;
        }

        var background = resolver.Resolve(dictionary[BackgroundKey]) as PdfDictionary;
        var lighting = resolver.Resolve(dictionary[LightingKey]) as PdfDictionary;
        var camera = Pdf3DCamera.From(dictionary[CameraKey], resolver);
        var source = PdfValues.Name(dictionary[MatrixSourceKey], resolver);
        return new Pdf3DView
        {
            Name = ReadText(dictionary[ExternalNameKey], resolver),
            InternalName = ReadText(dictionary[InternalNameKey], resolver),
            UsesCamera = source == MatrixFromCamera || (source == null && camera != null),
            Camera = camera,
            ViewNode = ReadText(dictionary[ViewNodeKey], resolver),
            CenterOfOrbit = PdfValues.Number(dictionary[CenterOfOrbitKey], resolver),
            Projection = Pdf3DProjection.From(dictionary[ProjectionKey], resolver),
            Background = Pdf3DColors.From(background?[BackgroundColorKey], resolver) ?? new Pdf3DColor(1, 1, 1),
            RenderMode = Pdf3DRenderMode.From(dictionary[RenderModeKey], resolver),
            Lighting = lighting != null ? PdfValues.Name(lighting[SubtypeKey], resolver) : null,
        };
    }

    private static string ReadText(PdfObject value, IPdfObjectResolver resolver) => resolver.Resolve(value) switch
    {
        PdfString text => text.ToText(),
        PdfArray { Count: > 0 } path => (resolver.Resolve(path[0]) as PdfString)?.ToText(),
        _ => null,
    };
}
