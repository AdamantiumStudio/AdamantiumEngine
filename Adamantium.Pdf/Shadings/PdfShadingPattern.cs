using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Shadings;

/// <summary>A shading pattern (type 2): the area is painted with a shading in pattern space.</summary>
public sealed class PdfShadingPattern : PdfPattern
{
    private PdfShadingPattern(PdfMatrix matrix, PdfShading shading, PdfGraphicsStateParameters parameters)
        : base(matrix)
    {
        Shading = shading;
        Parameters = parameters;
    }

    public PdfShading Shading { get; }

    /// <summary>The graphics state the pattern is painted with; null when it sets none.</summary>
    public PdfGraphicsStateParameters Parameters { get; }

    internal static PdfShadingPattern Read(PdfDictionary dictionary, PdfMatrix matrix, PdfResources resources,
        PdfDocument document)
    {
        var shading = PdfShading.Create(dictionary[PdfShadingKeys.Shading], resources, document);
        if (shading == null)
        {
            return null;
        }

        var parameters = document.Resolve(dictionary[PdfShadingKeys.ExtGState]) is PdfDictionary state
            ? PdfGraphicsStateParameters.From(state, resources, document)
            : null;
        return new PdfShadingPattern(matrix, shading, parameters);
    }
}
