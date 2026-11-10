using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Shadings;

/// <summary>A tiling pattern (type 1): a cell drawn by its own content stream, repeated every <see cref="XStep"/> and
/// <see cref="YStep"/> across pattern space.</summary>
public sealed class PdfTilingPattern : PdfPattern
{
    private PdfTilingPattern(PdfMatrix matrix, PdfStream cell, PdfResources resources, bool isColored,
        int tilingType, PdfRectangle box, double xStep, double yStep)
        : base(matrix)
    {
        Cell = cell;
        Resources = resources;
        IsColored = isColored;
        TilingType = tilingType;
        BBox = box;
        XStep = xStep;
        YStep = yStep;
    }

    /// <summary>The content stream drawing one cell.</summary>
    public PdfStream Cell { get; }

    /// <summary>What the cell's stream names; the resources the pattern was found in when it has none of its own.
    /// </summary>
    public PdfResources Resources { get; }

    /// <summary>Whether the cell gives its own colors (paint type 1), rather than being a stencil painted with the color
    /// given beside the pattern (paint type 2).</summary>
    public bool IsColored { get; }

    /// <summary>1 constant spacing, 2 no distortion, 3 faster tiling - how strictly the cells keep their spacing on the
    /// device.</summary>
    public int TilingType { get; }

    /// <summary>The cell's box in pattern space; what is drawn outside it is cut.</summary>
    public PdfRectangle BBox { get; }

    public double XStep { get; }

    public double YStep { get; }

    internal static PdfTilingPattern Read(PdfStream cell, PdfMatrix matrix, PdfResources resources,
        PdfDocument document)
    {
        var dictionary = cell.Dictionary;
        var box = PdfRectangle.From(dictionary[PdfShadingKeys.BBox], document);
        var xStep = PdfValues.Number(dictionary[PdfShadingKeys.XStep], document);
        var yStep = PdfValues.Number(dictionary[PdfShadingKeys.YStep], document);
        if (box is not { } cellBox || xStep == 0 || yStep == 0)
        {
            return null;
        }

        var own = document.Resolve(dictionary[PdfShadingKeys.Resources]) is PdfDictionary written
            ? new PdfResources(written, document)
            : resources;
        return new PdfTilingPattern(matrix, cell, own,
            PdfValues.Integer(dictionary[PdfShadingKeys.PaintType], document, 1) != 2,
            PdfValues.Integer(dictionary[PdfShadingKeys.TilingType], document, 1), cellBox, xStep, yStep);
    }
}
