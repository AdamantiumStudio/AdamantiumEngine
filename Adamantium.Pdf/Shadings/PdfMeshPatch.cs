using Adamantium.Pdf.Document;

namespace Adamantium.Pdf.Shadings;

/// <summary>A tensor-product patch of a patch mesh shading (ISO 32000-2, 8.7.4.5.8): sixteen control points, the point
/// p<sub>ij</sub> at <c>i * 4 + j</c>, and the color values at its corners p00, p03, p33 and p30 in that order. A Coons
/// patch (type 6) is given as the tensor patch it equals, its four inner points worked out from its edges.</summary>
public sealed class PdfMeshPatch
{
    public PdfMeshPatch(PdfPoint[] points, double[][] corners)
    {
        Points = points;
        Corners = corners;
    }

    public PdfPoint[] Points { get; }

    public double[][] Corners { get; }

    public PdfPoint this[int i, int j] => Points[i * 4 + j];
}
