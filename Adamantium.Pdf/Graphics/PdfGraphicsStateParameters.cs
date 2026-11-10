using Adamantium.Pdf.Document;
using Adamantium.Pdf.Fonts;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Graphics;

/// <summary>A graphics state parameter dictionary (ISO 32000-2, 8.4.5), the operand of <c>gs</c>: the parameters it
/// sets, each null when the dictionary leaves it as it is. The device-dependent ones - halftones, transfer and
/// generation functions, overprint, flatness, smoothness - are not read: a screen does not show them.</summary>
public sealed class PdfGraphicsStateParameters
{
    private PdfGraphicsStateParameters()
    {
    }

    public double? LineWidth { get; private init; }

    /// <summary>0 butt, 1 round, 2 projecting square.</summary>
    public int? LineCap { get; private init; }

    /// <summary>0 miter, 1 round, 2 bevel.</summary>
    public int? LineJoin { get; private init; }

    public double? MiterLimit { get; private init; }

    public PdfDashPattern? Dash { get; private init; }

    public string RenderingIntent { get; private init; }

    /// <summary>The font <see cref="FontSize"/> goes with; null when the dictionary sets none, or it cannot be read.
    /// </summary>
    public PdfFont Font { get; private init; }

    public double? FontSize { get; private init; }

    /// <summary>The first mode of <c>/BM</c> this reader knows - a file may list several, newest first; Normal when it
    /// knows none of them, as <c>Compatible</c> is.</summary>
    public PdfBlendMode? BlendMode { get; private init; }

    /// <summary>Whether <c>/SMask</c> is written: then <see cref="SoftMask"/> replaces the mask, null for none.</summary>
    public bool SetsSoftMask { get; private init; }

    public PdfSoftMask SoftMask { get; private init; }

    /// <summary>The opacity of strokes, 0 to 1.</summary>
    public double? StrokeAlpha { get; private init; }

    /// <summary>The opacity of everything else, 0 to 1.</summary>
    public double? FillAlpha { get; private init; }

    public bool? AlphaIsShape { get; private init; }

    public bool? TextKnockout { get; private init; }

    /// <summary>The parameters a dictionary sets; <paramref name="resources"/> are what its soft mask's color spaces are
    /// named in.</summary>
    public static PdfGraphicsStateParameters From(PdfDictionary dictionary, PdfResources resources,
        PdfDocument document)
    {
        dictionary ??= new PdfDictionary();
        PdfFont font = null;
        double? fontSize = null;
        if (document.Resolve(dictionary[PdfGraphicsKeys.Font]) is PdfArray { Count: >= 2 } fontEntry)
        {
            font = PdfFonts.Create(fontEntry[0], document);
            fontSize = PdfValues.Number(fontEntry[1], document);
        }

        PdfDashPattern? dash = null;
        if (document.Resolve(dictionary[PdfGraphicsKeys.Dash]) is PdfArray { Count: >= 2 } dashEntry)
        {
            dash = PdfDashPattern.From(dashEntry[0], PdfValues.Number(dashEntry[1], document), document);
        }

        var softMask = dictionary[PdfGraphicsKeys.SoftMask];
        return new PdfGraphicsStateParameters
        {
            LineWidth = Number(dictionary[PdfGraphicsKeys.LineWidth], document, 0, double.MaxValue),
            LineCap = Choice(dictionary[PdfGraphicsKeys.LineCap], document),
            LineJoin = Choice(dictionary[PdfGraphicsKeys.LineJoin], document),
            MiterLimit = Number(dictionary[PdfGraphicsKeys.MiterLimit], document, 1, double.MaxValue),
            Dash = dash,
            RenderingIntent = PdfValues.Name(dictionary[PdfGraphicsKeys.RenderingIntent], document),
            Font = font,
            FontSize = fontSize,
            BlendMode = dictionary[PdfGraphicsKeys.BlendMode] != null
                ? BlendModeOf(dictionary[PdfGraphicsKeys.BlendMode], document)
                : null,
            SetsSoftMask = softMask != null,
            SoftMask = softMask != null ? PdfSoftMask.From(softMask, resources, document) : null,
            StrokeAlpha = Number(dictionary[PdfGraphicsKeys.StrokeAlpha], document, 0, 1),
            FillAlpha = Number(dictionary[PdfGraphicsKeys.FillAlpha], document, 0, 1),
            AlphaIsShape = Flag(dictionary[PdfGraphicsKeys.AlphaIsShape], document),
            TextKnockout = Flag(dictionary[PdfGraphicsKeys.TextKnockout], document),
        };
    }

    /// <summary>The mode a <c>/BM</c> name or array of names stands for.</summary>
    public static PdfBlendMode BlendModeOf(PdfObject value, IPdfObjectResolver resolver)
    {
        if (resolver.Resolve(value) is PdfArray names)
        {
            foreach (var name in names)
            {
                if (Known(PdfValues.Name(name, resolver)) is { } mode)
                {
                    return mode;
                }
            }

            return PdfBlendMode.Normal;
        }

        return Known(PdfValues.Name(value, resolver)) ?? PdfBlendMode.Normal;
    }

    private static PdfBlendMode? Known(string name) => name switch
    {
        "Normal" or "Compatible" => PdfBlendMode.Normal,
        "Multiply" => PdfBlendMode.Multiply,
        "Screen" => PdfBlendMode.Screen,
        "Overlay" => PdfBlendMode.Overlay,
        "Darken" => PdfBlendMode.Darken,
        "Lighten" => PdfBlendMode.Lighten,
        "ColorDodge" => PdfBlendMode.ColorDodge,
        "ColorBurn" => PdfBlendMode.ColorBurn,
        "HardLight" => PdfBlendMode.HardLight,
        "SoftLight" => PdfBlendMode.SoftLight,
        "Difference" => PdfBlendMode.Difference,
        "Exclusion" => PdfBlendMode.Exclusion,
        "Hue" => PdfBlendMode.Hue,
        "Saturation" => PdfBlendMode.Saturation,
        "Color" => PdfBlendMode.Color,
        "Luminosity" => PdfBlendMode.Luminosity,
        _ => null,
    };

    private static double? Number(PdfObject value, IPdfObjectResolver resolver, double min, double max) =>
        resolver.Resolve(value) is PdfInteger or PdfReal ? System.Math.Clamp(PdfValues.Number(value, resolver), min, max)
            : null;

    private static int? Choice(PdfObject value, IPdfObjectResolver resolver) =>
        resolver.Resolve(value) is PdfInteger or PdfReal ? System.Math.Clamp(PdfValues.Integer(value, resolver), 0, 2)
            : null;

    private static bool? Flag(PdfObject value, IPdfObjectResolver resolver) =>
        resolver.Resolve(value) is PdfBoolean boolean ? boolean.Value : null;
}
