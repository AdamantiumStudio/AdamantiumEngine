using Adamantium.Pdf.Document;
using Adamantium.Pdf.Functions;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Graphics;

/// <summary>A soft mask dictionary (ISO 32000-2, 11.6.5.2): a group whose alpha or luminosity lets later drawing through.
/// </summary>
public sealed class PdfSoftMask
{
    private PdfSoftMask(PdfSoftMaskKind kind, PdfStream group, PdfTransparencyGroup transparency, double[] backdrop,
        PdfFunction transfer)
    {
        Kind = kind;
        Group = group;
        Transparency = transparency;
        Backdrop = backdrop;
        Transfer = transfer;
    }

    public PdfSoftMaskKind Kind { get; }

    /// <summary>The form drawing the mask.</summary>
    public PdfStream Group { get; }

    /// <summary>The form's transparency group: the space <see cref="Backdrop"/> is in. Null when the form has none.
    /// </summary>
    public PdfTransparencyGroup Transparency { get; }

    /// <summary>The color a luminosity mask is drawn over, in the group's space; null for black.</summary>
    public double[] Backdrop { get; }

    /// <summary>What the mask's values go through before use; null for none.</summary>
    public PdfFunction Transfer { get; }

    /// <summary>The soft mask of a <c>/SMask</c> entry; null for <c>/None</c> and for one that is not a mask - no form,
    /// an unknown kind.</summary>
    public static PdfSoftMask From(PdfObject value, PdfResources resources, PdfDocument document)
    {
        if (document.Resolve(value) is not PdfDictionary mask ||
            document.Resolve(mask[PdfGraphicsKeys.Group]) is not PdfStream group)
        {
            return null;
        }

        PdfSoftMaskKind kind;
        switch (PdfValues.Name(mask[PdfGraphicsKeys.Kind], document))
        {
            case PdfGraphicsKeys.Alpha:
                kind = PdfSoftMaskKind.Alpha;
                break;
            case PdfGraphicsKeys.Luminosity:
                kind = PdfSoftMaskKind.Luminosity;
                break;
            default:
                return null;
        }

        var transfer = PdfValues.Name(mask[PdfGraphicsKeys.Transfer], document) == PdfGraphicsKeys.Identity
            ? null
            : mask[PdfGraphicsKeys.Transfer] is { } written ? PdfFunctions.Create(written, document) : null;
        var groupResources = document.Resolve(group.Dictionary[PdfKeys.Resources]) is PdfDictionary own
            ? new PdfResources(own, document)
            : resources;
        return new PdfSoftMask(kind, group, PdfTransparencyGroup.Of(group, groupResources, document),
            PdfValues.Numbers(mask[PdfGraphicsKeys.Backdrop], document), transfer);
    }
}
