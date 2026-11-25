using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Graphics;

/// <summary>A form's transparency group (ISO 32000-2, 11.6.6): the form is drawn apart and laid down as one object.
/// Read from a form (<see cref="Of"/>), or given to one written (<see cref="Writing.PdfContentBuilder.ToFormXObject(PdfRectangle, PdfTransparencyGroup)"/>).
/// </summary>
public sealed class PdfTransparencyGroup
{
    /// <summary>A group blended in <paramref name="colorSpace"/> - for one written, DeviceGray, DeviceRGB or
    /// DeviceCMYK - starting from nothing (<paramref name="isolated"/>) or from what lies under it.</summary>
    public PdfTransparencyGroup(PdfColorSpace colorSpace, bool isolated = true, bool knockout = false)
    {
        ColorSpace = colorSpace;
        Isolated = isolated;
        Knockout = knockout;
    }

    /// <summary>An isolated group in DeviceRGB - a layer: what it draws is mixed first, then faded or masked as one.
    /// </summary>
    public static PdfTransparencyGroup Layer { get; } = new(PdfDeviceRgbColorSpace.Instance);

    /// <summary>An isolated group in DeviceGray - a soft mask's picture read by its luminosity.</summary>
    public static PdfTransparencyGroup GrayMask { get; } = new(PdfDeviceGrayColorSpace.Instance);

    /// <summary>The space the group is blended in; null when it does not say, and the space it is drawn into is used.
    /// </summary>
    public PdfColorSpace ColorSpace { get; }

    /// <summary>Whether the group starts from nothing rather than from what lies under it.</summary>
    public bool Isolated { get; }

    /// <summary>Whether each object of the group is laid over the group's start, not over the objects before it.
    /// </summary>
    public bool Knockout { get; }

    /// <summary>The group of a form; null when the form has none, or its <c>/Group</c> is not a transparency group.
    /// </summary>
    public static PdfTransparencyGroup Of(PdfStream form, PdfResources resources, PdfDocument document)
    {
        if (document.Resolve(form?.Dictionary[PdfGraphicsKeys.FormGroup]) is not PdfDictionary group ||
            PdfValues.Name(group[PdfGraphicsKeys.Kind], document) != PdfGraphicsKeys.Transparency)
        {
            return null;
        }

        var space = group[PdfGraphicsKeys.ColorSpace] != null
            ? PdfColorSpaces.Create(group[PdfGraphicsKeys.ColorSpace], resources, document)
            : null;
        return new PdfTransparencyGroup(space, PdfValues.Boolean(group[PdfGraphicsKeys.Isolated], document),
            PdfValues.Boolean(group[PdfGraphicsKeys.Knockout], document));
    }
}
