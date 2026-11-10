using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Functions;

namespace Adamantium.Pdf.Shadings;

/// <summary>A radial shading (type 3): color varies over the circles that run from the start circle to the end circle,
/// centers and radii both moving in step; a later circle paints over an earlier one.</summary>
public sealed class PdfRadialShading : PdfGradientShading
{
    internal PdfRadialShading(PdfColorSpace colorSpace, double[] background, PdfRectangle? box, bool antiAlias,
        PdfFunction function, double t0, double t1, bool extendStart, bool extendEnd, PdfPoint startCenter,
        double startRadius, PdfPoint endCenter, double endRadius)
        : base(3, colorSpace, background, box, antiAlias, function, t0, t1, extendStart, extendEnd)
    {
        StartCenter = startCenter;
        StartRadius = startRadius;
        EndCenter = endCenter;
        EndRadius = endRadius;
    }

    public PdfPoint StartCenter { get; }

    public double StartRadius { get; }

    public PdfPoint EndCenter { get; }

    public double EndRadius { get; }
}
