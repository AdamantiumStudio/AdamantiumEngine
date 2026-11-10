using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Functions;

namespace Adamantium.Pdf.Shadings;

/// <summary>An axial shading (type 2): color varies along the axis from <see cref="Start"/> to <see cref="End"/> and is
/// the same along every line across it.</summary>
public sealed class PdfAxialShading : PdfGradientShading
{
    internal PdfAxialShading(PdfColorSpace colorSpace, double[] background, PdfRectangle? box, bool antiAlias,
        PdfFunction function, double t0, double t1, bool extendStart, bool extendEnd, PdfPoint start, PdfPoint end)
        : base(2, colorSpace, background, box, antiAlias, function, t0, t1, extendStart, extendEnd)
    {
        Start = start;
        End = end;
    }

    public PdfPoint Start { get; }

    public PdfPoint End { get; }
}
