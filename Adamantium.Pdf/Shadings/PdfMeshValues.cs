using System;
using Adamantium.Pdf.Document;

namespace Adamantium.Pdf.Shadings;

/// <summary>How a mesh shading packs its values (ISO 32000-2, 8.7.4.5.5 to 8.7.4.5.8): the bits of a flag, a coordinate
/// and a color value, and the ranges the <c>/Decode</c> array maps them to.</summary>
internal sealed class PdfMeshValues(int coordinateBits, int componentBits, int flagBits, int count, double[] decode)
{
    public int CoordinateBits { get; } = coordinateBits;

    public int FlagBits { get; } = flagBits;

    public long ColorBits { get; } = (long)componentBits * count;

    public long VertexBits => FlagBits + 2L * CoordinateBits + ColorBits;

    public PdfPoint Point(ref PdfBitReader bits) =>
        new(Decode(bits.Read(CoordinateBits), CoordinateBits, decode[0], decode[1]),
            Decode(bits.Read(CoordinateBits), CoordinateBits, decode[2], decode[3]));

    public double[] Color(ref PdfBitReader bits)
    {
        var color = new double[count];
        for (var i = 0; i < count; i++)
        {
            color[i] = Decode(bits.Read(componentBits), componentBits, decode[4 + 2 * i], decode[5 + 2 * i]);
        }

        return color;
    }

    public PdfMeshVertex Vertex(ref PdfBitReader bits) => new(Point(ref bits), Color(ref bits));

    private static double Decode(uint raw, int bits, double min, double max) =>
        min + raw * (max - min) / (Math.Pow(2, bits) - 1);
}
