using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Functions;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Shadings;

/// <summary>An axial (type 2) or radial (type 3) shading: color is a function of one parameter t, running from
/// <see cref="T0"/> at the start of the axis or the first circle to <see cref="T1"/> at its end or the second.</summary>
public abstract class PdfGradientShading : PdfShading
{
    private protected PdfGradientShading(int type, PdfColorSpace colorSpace, double[] background, PdfRectangle? box,
        bool antiAlias, PdfFunction function, double t0, double t1, bool extendStart, bool extendEnd)
        : base(type, colorSpace, background, box, antiAlias, function)
    {
        T0 = t0;
        T1 = t1;
        ExtendStart = extendStart;
        ExtendEnd = extendEnd;
    }

    public double T0 { get; }

    public double T1 { get; }

    /// <summary>Whether the start color goes on before the start.</summary>
    public bool ExtendStart { get; }

    /// <summary>Whether the end color goes on past the end.</summary>
    public bool ExtendEnd { get; }

    internal static PdfGradientShading Read(int type, PdfDictionary dictionary, PdfDocument document,
        PdfColorSpace space, double[] background, PdfRectangle? box, bool antiAlias, PdfFunction function)
    {
        var coords = PdfValues.Numbers(dictionary[PdfShadingKeys.Coords], document);
        if (function == null || function.Inputs != 1 || coords == null || coords.Length != (type == 2 ? 4 : 6))
        {
            return null;
        }

        var domain = PdfValues.Numbers(dictionary[PdfShadingKeys.Domain], document);
        var (t0, t1) = domain is { Length: 2 } ? (domain[0], domain[1]) : (0.0, 1.0);
        var extend = document.Resolve(dictionary[PdfShadingKeys.Extend]) as PdfArray;
        var extendStart = extend is { Count: 2 } && PdfValues.Boolean(extend[0], document);
        var extendEnd = extend is { Count: 2 } && PdfValues.Boolean(extend[1], document);
        if (type == 2)
        {
            return new PdfAxialShading(space, background, box, antiAlias, function, t0, t1, extendStart, extendEnd,
                new PdfPoint(coords[0], coords[1]), new PdfPoint(coords[2], coords[3]));
        }

        if (coords[2] < 0 || coords[5] < 0)
        {
            return null;
        }

        return new PdfRadialShading(space, background, box, antiAlias, function, t0, t1, extendStart, extendEnd,
            new PdfPoint(coords[0], coords[1]), coords[2], new PdfPoint(coords[3], coords[4]), coords[5]);
    }
}
