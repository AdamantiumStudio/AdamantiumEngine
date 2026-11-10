using System;
using System.Collections.Generic;
using Adamantium.Pdf.ColorSpaces;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Functions;
using Adamantium.Pdf.Objects;

namespace Adamantium.Pdf.Shadings;

/// <summary>A mesh shading (types 4 to 7), read out of its stream: a free-form (4) or lattice (5) mesh of triangles, or
/// a mesh of Coons (6) or tensor-product (7) patches. Reading stops at the first vertex or patch the data cuts short,
/// and after <see cref="MaxVertices"/> vertices or <see cref="MaxPatches"/> patches.</summary>
public sealed class PdfMeshShading : PdfShading
{
    public const int MaxVertices = 1 << 20;
    public const int MaxPatches = 1 << 18;

    // The order a patch's points are written in (ISO 32000-2, 8.7.4.5.7 and 8.7.4.5.8), as indices i * 4 + j: the
    // twelve of the boundary from p00 round, then the four inner ones of a tensor patch.
    private static readonly int[] PointOrder = [0, 1, 2, 3, 7, 11, 15, 14, 13, 12, 8, 4, 5, 6, 10, 9];

    private PdfMeshShading(int type, PdfColorSpace colorSpace, double[] background, PdfRectangle? box, bool antiAlias,
        PdfFunction function, List<PdfMeshTriangle> triangles, List<PdfMeshPatch> patches)
        : base(type, colorSpace, background, box, antiAlias, function)
    {
        Triangles = triangles;
        Patches = patches;
    }

    /// <summary>The triangles of a type 4 or 5 mesh; empty for a patch mesh.</summary>
    public IReadOnlyList<PdfMeshTriangle> Triangles { get; }

    /// <summary>The patches of a type 6 or 7 mesh; empty for a triangle mesh.</summary>
    public IReadOnlyList<PdfMeshPatch> Patches { get; }

    internal static PdfMeshShading Read(int type, PdfStream stream, PdfDocument document, PdfColorSpace space,
        double[] background, PdfRectangle? box, bool antiAlias, PdfFunction function)
    {
        var dictionary = stream.Dictionary;
        var coordinateBits = PdfValues.Integer(dictionary[PdfShadingKeys.BitsPerCoordinate], document);
        var componentBits = PdfValues.Integer(dictionary[PdfShadingKeys.BitsPerComponent], document);
        var flagBits = type == 5 ? 0 : PdfValues.Integer(dictionary[PdfShadingKeys.BitsPerFlag], document);
        var values = function != null ? 1 : space.Components;
        var decode = PdfValues.Numbers(dictionary[PdfShadingKeys.Decode], document);
        if (coordinateBits is < 1 or > 32 || componentBits is < 1 or > 16 || flagBits is < 0 or > 8 ||
            (type != 5 && flagBits == 0) || decode == null || decode.Length < 4 + 2 * values ||
            (function != null && function.Inputs != 1))
        {
            return null;
        }

        var reader = new PdfMeshValues(coordinateBits, componentBits, flagBits, values, decode);
        var data = document.Decode(stream).Data.Span;
        var triangles = new List<PdfMeshTriangle>();
        var patches = new List<PdfMeshPatch>();
        switch (type)
        {
            case 4:
                FreeForm(data, reader, triangles);
                break;
            case 5:
                var perRow = PdfValues.Integer(dictionary[PdfShadingKeys.VerticesPerRow], document);
                if (perRow < 2)
                {
                    return null;
                }

                Lattice(data, reader, perRow, triangles);
                break;
            default:
                Patched(type, data, reader, patches);
                break;
        }

        return new PdfMeshShading(type, space, background, box, antiAlias, function, triangles, patches);
    }

    private static void FreeForm(ReadOnlySpan<byte> data, PdfMeshValues values, List<PdfMeshTriangle> triangles)
    {
        var bits = new PdfBitReader(data);
        PdfMeshVertex a = default, b = default, c = default;
        var pending = 0;
        for (var read = 0; read < MaxVertices && bits.Has(values.VertexBits); read++)
        {
            var flag = bits.Read(values.FlagBits);
            var vertex = values.Vertex(ref bits);
            bits.Align();
            if (pending > 0)
            {
                (pending == 2 ? ref b : ref c) = vertex;
                if (--pending == 0)
                {
                    triangles.Add(new PdfMeshTriangle(a, b, c));
                }

                continue;
            }

            switch (flag)
            {
                case 0:
                    a = vertex;
                    pending = 2;
                    break;
                case 1 when triangles.Count > 0:
                    (a, b, c) = (b, c, vertex);
                    triangles.Add(new PdfMeshTriangle(a, b, c));
                    break;
                case 2 when triangles.Count > 0:
                    (b, c) = (c, vertex);
                    triangles.Add(new PdfMeshTriangle(a, b, c));
                    break;
                default:
                    return;
            }
        }
    }

    // Rows are kept as long as the data has vertices for, not as long as /VerticesPerRow claims.
    private static void Lattice(ReadOnlySpan<byte> data, PdfMeshValues values, int perRow,
        List<PdfMeshTriangle> triangles)
    {
        var bits = new PdfBitReader(data);
        var capacity = (int)Math.Min(perRow, Math.Min(MaxVertices, data.Length * 8L / Math.Max(values.VertexBits, 1) + 1));
        var above = new List<PdfMeshVertex>(capacity);
        var row = new List<PdfMeshVertex>(capacity);
        for (var read = 0; read < MaxVertices && bits.Has(values.VertexBits); read++)
        {
            row.Add(values.Vertex(ref bits));
            if (row.Count < perRow)
            {
                continue;
            }

            if (above.Count == perRow)
            {
                for (var i = 0; i + 1 < perRow; i++)
                {
                    triangles.Add(new PdfMeshTriangle(above[i], above[i + 1], row[i]));
                    triangles.Add(new PdfMeshTriangle(above[i + 1], row[i + 1], row[i]));
                }
            }

            (above, row) = (row, above);
            row.Clear();
        }
    }

    private static void Patched(int type, ReadOnlySpan<byte> data, PdfMeshValues values, List<PdfMeshPatch> patches)
    {
        var bits = new PdfBitReader(data);
        var points = type == 7 ? 16 : 12;
        PdfMeshPatch previous = null;
        while (patches.Count < MaxPatches && bits.Has(values.FlagBits))
        {
            var flag = (int)bits.Read(values.FlagBits);
            if (flag > 3 || (flag != 0 && previous == null))
            {
                return;
            }

            var newPoints = flag == 0 ? points : points - 4;
            var newCorners = flag == 0 ? 4 : 2;
            if (!bits.Has((long)newPoints * 2 * values.CoordinateBits + (long)newCorners * values.ColorBits))
            {
                return;
            }

            var patch = new PdfPoint[16];
            var corners = new double[4][];
            var first = 0;
            if (flag != 0)
            {
                for (var k = 0; k < 4; k++)
                {
                    patch[PointOrder[k]] = previous.Points[PointOrder[(3 * flag + k) % 12]];
                }

                corners[0] = previous.Corners[flag];
                corners[1] = previous.Corners[(flag + 1) % 4];
                first = 4;
            }

            for (var k = first; k < points; k++)
            {
                patch[PointOrder[k]] = values.Point(ref bits);
            }

            for (var k = 4 - newCorners; k < 4; k++)
            {
                corners[k] = values.Color(ref bits);
            }

            bits.Align();
            if (type == 6)
            {
                CoonsInterior(patch);
            }

            previous = new PdfMeshPatch(patch, corners);
            patches.Add(previous);
        }
    }

    // The inner points of the tensor patch a Coons patch equals (ISO 32000-2, 8.7.4.5.8).
    private static void CoonsInterior(PdfPoint[] p)
    {
        p[5] = Inner(p[0], p[1], p[4], p[3], p[12], p[13], p[7], p[15]);
        p[6] = Inner(p[3], p[2], p[7], p[0], p[15], p[14], p[4], p[12]);
        p[9] = Inner(p[12], p[13], p[8], p[15], p[0], p[1], p[11], p[3]);
        p[10] = Inner(p[15], p[14], p[11], p[12], p[3], p[2], p[8], p[0]);
    }

    // 1/9 (-4 corner + 6 (along1 + along2) - 2 (side1 + side2) + 3 (far1 + far2) - opposite).
    private static PdfPoint Inner(PdfPoint corner, PdfPoint along1, PdfPoint along2, PdfPoint side1, PdfPoint side2,
        PdfPoint far1, PdfPoint far2, PdfPoint opposite) =>
        new((-4 * corner.X + 6 * (along1.X + along2.X) - 2 * (side1.X + side2.X) + 3 * (far1.X + far2.X) -
             opposite.X) / 9,
            (-4 * corner.Y + 6 * (along1.Y + along2.Y) - 2 * (side1.Y + side2.Y) + 3 * (far1.Y + far2.Y) -
             opposite.Y) / 9);
}
