using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Functions;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Shadings;

/// <summary>A function-based shading (type 1): the color at each point of <see cref="Domain"/> is the function of its two
/// coordinates; <see cref="Matrix"/> puts the domain in the shading's space.</summary>
public sealed class PdfFunctionShading : PdfShading
{
    private PdfFunctionShading(PdfColorSpace colorSpace, double[] background, PdfRectangle? box, bool antiAlias,
        PdfFunction function, double[] domain, PdfMatrix matrix)
        : base(1, colorSpace, background, box, antiAlias, function)
    {
        Domain = domain;
        Matrix = matrix;
    }

    /// <summary>x0, x1, y0, y1.</summary>
    public double[] Domain { get; }

    public PdfMatrix Matrix { get; }

    internal static PdfFunctionShading Read(PdfDictionary dictionary, PdfDocument document, PdfColorSpace space,
        double[] background, PdfRectangle? box, bool antiAlias, PdfFunction function)
    {
        if (function == null || function.Inputs != 2)
        {
            return null;
        }

        var domain = PdfValues.Numbers(dictionary[PdfShadingKeys.Domain], document);
        if (domain is not { Length: 4 })
        {
            domain = [0, 1, 0, 1];
        }

        return new PdfFunctionShading(space, background, box, antiAlias, function, domain,
            PdfMatrix.From(PdfValues.Numbers(dictionary[PdfShadingKeys.Matrix], document)));
    }
}
