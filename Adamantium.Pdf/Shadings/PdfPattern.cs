using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;

namespace Adamantium.Pdf.Shadings;

/// <summary>A pattern (ISO 32000-2, 8.7.3): what an area is painted with instead of a color, laid in pattern space,
/// which <see cref="Matrix"/> maps to the default space of the page or form the pattern is a resource of.</summary>
public abstract class PdfPattern
{
    private protected PdfPattern(PdfMatrix matrix)
    {
        Matrix = matrix;
    }

    public PdfMatrix Matrix { get; }

    /// <summary>The pattern of a pattern resource; null when it is not one.</summary>
    public static PdfPattern Create(PdfObject value, PdfResources resources, PdfDocument document)
    {
        var resolved = document.Resolve(value);
        var dictionary = resolved as PdfDictionary ?? (resolved as PdfStream)?.Dictionary;
        if (dictionary == null)
        {
            return null;
        }

        var matrix = PdfMatrix.From(PdfValues.Numbers(dictionary[PdfShadingKeys.Matrix], document));
        return PdfValues.Integer(dictionary[PdfShadingKeys.PatternType], document) switch
        {
            1 when resolved is PdfStream cell => PdfTilingPattern.Read(cell, matrix, resources, document),
            2 => PdfShadingPattern.Read(dictionary, matrix, resources, document),
            _ => null,
        };
    }
}
