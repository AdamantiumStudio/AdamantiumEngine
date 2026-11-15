using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Engine.Compiler.Models.Prc;
using NUnit.Framework;

namespace Adamantium.Engine.CompilerTests;

/// <summary>PRC highly compressed tessellation (PRC_TYPE_TESS_3D_Compressed) read back from files the test encoder
/// writes: Huffman coded and plain arrays, traversals over closed and open meshes and over disjoint parts, normals
/// stored and recalculated, planar faces, colours, line attributes and texture coordinates. Points must come back within
/// the tolerance they were stored to, triangles with their winding, and normals within their angles' step.</summary>
[TestFixture]
public class PrcCompressedTessellationTests
{
    private const double Tolerance = 0.01;

    [Test]
    public void HuffmanCharacterArray_ReadsBackItsValues([Values(2, 6, 8)] int bitNumber)
    {
        var limit = 1 << bitNumber;
        var values = Enumerable.Range(0, 300).Select(index => index * 7 % 5 == 0 ? 1 : index % 3 == 0 ? 0 : (index * 13) % limit).ToList();
        var writer = new PrcTestBitWriter();
        PrcTestArrayWriter.CharacterArray(writer, values, bitNumber, true);
        writer.WriteUnsignedInteger(77);
        var bits = new PrcBitReader(writer.ToArray(), "test");

        var read = bits.ReadCharacterArray(bitNumber, false, "values");

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(values));
            Assert.That(bits.ReadUnsignedInteger(), Is.EqualTo(77u), "the stream goes on after the array");
        });
    }

    [Test]
    public void HuffmanArrayOfOneValue_ReadsBack()
    {
        var writer = new PrcTestBitWriter();
        PrcTestArrayWriter.CharacterArray(writer, Enumerable.Repeat(5, 40).ToList(), 6, true);

        var read = new PrcBitReader(writer.ToArray(), "test").ReadCharacterArray(6, false, "values");

        Assert.That(read, Is.EqualTo(Enumerable.Repeat(5, 40)));
    }

    [Test]
    public void ShortArray_ReadsBackPlainAndCoded([Values] bool huffman)
    {
        var values = new[] { 0, 1, 1023, 512, 512, 7, 1023, 0, 0, 0 };
        var writer = new PrcTestBitWriter();
        PrcTestArrayWriter.ShortArray(writer, values, 10, huffman);

        var read = new PrcBitReader(writer.ToArray(), "test").ReadShortArray(10, "values");

        Assert.That(read, Is.EqualTo(values));
    }

    [Test]
    public void CompressedArrays_ReadBackIntegersAndIndicesWithNegativeSteps([Values] bool huffman)
    {
        var integers = new[] { 0, -1, 1, 1000, -70000, 3, 0, 123456 };
        var indices = new[] { 300, 2, 5, 4, 1000, 999, 0, 0, 65536 };
        var writer = new PrcTestBitWriter();
        PrcTestArrayWriter.CompressedIntegerArray(writer, integers, huffman);
        PrcTestArrayWriter.CompressedIndiceArray(writer, indices, huffman);
        var bits = new PrcBitReader(writer.ToArray(), "test");

        Assert.Multiple(() =>
        {
            Assert.That(bits.ReadCompressedIntegerArray("integers"), Is.EqualTo(integers));
            Assert.That(bits.ReadCompressedIndiceArray("indices"), Is.EqualTo(indices));
        });
    }

    [Test]
    public void CorruptHuffmanCodes_Throw()
    {
        var writer = new PrcTestBitWriter();
        PrcTestArrayWriter.CharacterArray(writer, [1, 2, 3, 1, 2, 3], 6, true);
        var bytes = writer.ToArray();
        bytes[3] ^= 0xFF;

        Assert.Throws<InvalidDataException>(() => new PrcBitReader(bytes, "test").ReadCharacterArray(6, false, "values"));
    }

    [Test]
    public void Tetrahedron_ComesBackWithItsPointsTrianglesAndNormals([Values] bool huffman, [Values] bool planar)
    {
        var mesh = Tetrahedron(new[] { 0.0, 0.0, 0.0 }, planar ? 4 : 1);
        if (planar)
        {
            mesh.PlanarFaces = [true, true, true, true];
        }

        CheckRoundTrip(mesh, huffman);
    }

    [Test]
    public void QuadStrip_ComesBackWithColorsLineAttributesAndTextures([Values] bool huffman)
    {
        var mesh = Strip(6, 1, 2.5);
        mesh.FaceLineAttributes = [3];
        mesh.CornerColors = mesh.Triangles.Select(triangle => triangle.Select(point => new[] { (byte)(point * 20), (byte)(255 - point), (byte)7, (byte)200 }).ToArray()).ToList();
        mesh.CornerTextures = mesh.Triangles.Select(triangle => triangle.Select(point => new[] { point * 0.25f, point % 2 * 0.5f }).ToArray()).ToList();

        CheckRoundTrip(mesh, huffman);
    }

    [Test]
    public void TwoDisjointParts_AreBothTraversed([Values] bool huffman, [Values] bool recalculated, [Values] bool tripleEdgeStatus)
    {
        var mesh = Tetrahedron(new[] { -3.0, 0.0, 0.0 }, 1);
        var other = Tetrahedron(new[] { 4.0, 1.0, -2.0 }, 1);
        var offset = mesh.Points.Count;
        mesh.Points.AddRange(other.Points);
        for (var triangle = 0; triangle < other.Triangles.Count; triangle++)
        {
            var corners = other.Triangles[triangle];
            mesh.AddTriangle(corners[0] + offset, corners[1] + offset, corners[2] + offset, 1);
        }

        mesh.CornerNormals = recalculated ? null : mesh.Triangles.Select(triangle => FlatNormals(mesh, triangle)).ToList();

        CheckRoundTrip(mesh, huffman, tripleEdgeStatus);
    }

    [Test]
    public void LargeCurvedGrid_StaysWithinTolerance([Values] bool huffman)
    {
        var mesh = Strip(30, 12, 1.0);
        for (var point = 0; point < mesh.Points.Count; point++)
        {
            var coordinates = mesh.Points[point];
            mesh.Points[point] = [coordinates[0], coordinates[1], Math.Sin(coordinates[0] * 0.4) * 2 + Math.Cos(coordinates[1] * 0.7)];
        }

        mesh.CornerNormals = mesh.Triangles.Select(triangle => triangle.Select(point => SurfaceNormal(mesh.Points[point])).ToArray()).ToList();

        CheckRoundTrip(mesh, huffman);
    }

    private static void CheckRoundTrip(PrcTestMesh mesh, bool huffman, bool tripleEdgeStatus = false)
    {
        var section = PrcTestEntities.Tessellations([PrcTestCompressedTessellation.Entity(mesh, Tolerance, huffman, tripleEdgeStatus)]);
        var structure = new PrcFileStructure { Index = 0 };
        var notes = new PrcNotes();
        PrcTessellationReader.Read(new PrcBitReader(section, "test"), structure, notes);
        var tessellation = (Prc3DTessellation)structure.Tessellations.Single();
        var coordinates = tessellation.Coordinates;
        var originalOf = new int[coordinates.Length / 3];
        for (var point = 0; point < originalOf.Length; point++)
        {
            var nearest = Enumerable.Range(0, mesh.Points.Count).OrderBy(original => Distance(mesh.Points[original], coordinates, point)).First();
            Assert.That(Distance(mesh.Points[nearest], coordinates, point), Is.LessThanOrEqualTo(Tolerance), $"point {point}");
            originalOf[point] = nearest;
        }

        var expected = mesh.Triangles.Select((triangle, index) => Canonical(triangle) + $" face {mesh.Faces[index]}").OrderBy(text => text).ToList();
        var decoded = new List<string>();
        var normalStride = tessellation.MustRecalculateNormals ? 0 : 1;
        foreach (var face in tessellation.Faces)
        {
            var textures = face.TextureCoordinateIndexes > 0 ? 1 : 0;
            var corner = normalStride + textures + 1;
            for (var triangle = 0; triangle < face.SizesTriangulated[0]; triangle++)
            {
                var start = (int)face.StartTriangulated + triangle * corner * 3;
                var points = new int[3];
                for (var vertex = 0; vertex < 3; vertex++)
                {
                    points[vertex] = originalOf[tessellation.TriangulatedIndices[start + vertex * corner + corner - 1] / 3];
                }

                var original = mesh.Triangles.FindIndex(candidate => Canonical(candidate) == Canonical(points));
                Assert.That(original, Is.GreaterThanOrEqualTo(0), $"triangle {string.Join(",", points)} is one of the mesh's, wound as it is");
                decoded.Add(Canonical(points) + $" face {mesh.Faces[original]}");
                for (var vertex = 0; vertex < 3; vertex++)
                {
                    var at = start + vertex * corner;
                    var cornerOfOriginal = Array.IndexOf(mesh.Triangles[original], points[vertex]);
                    if (normalStride > 0)
                    {
                        var normal = (int)tessellation.TriangulatedIndices[at];
                        var wanted = mesh.CornerNormals[original][cornerOfOriginal];
                        var dot = wanted[0] * tessellation.Normals[normal] + wanted[1] * tessellation.Normals[normal + 1] + wanted[2] * tessellation.Normals[normal + 2];
                        Assert.That(dot, Is.GreaterThan(0.999), $"the normal of corner {vertex} of triangle {original}");
                    }

                    if (textures > 0)
                    {
                        var texture = (int)tessellation.TriangulatedIndices[at + normalStride];
                        Assert.That(new[] { tessellation.TextureCoordinates[texture], tessellation.TextureCoordinates[texture + 1] },
                            Is.EqualTo(mesh.CornerTextures[original][cornerOfOriginal].Select(value => (double)value)));
                    }

                    if (mesh.CornerColors != null)
                    {
                        var color = face.VertexColors[triangle * 3 + vertex];
                        Assert.That(new[] { color.R, color.G, color.B, color.A }, Is.EqualTo(mesh.CornerColors[original][cornerOfOriginal]));
                    }
                }

                if (mesh.FaceLineAttributes != null)
                {
                    Assert.That(face.LineAttributes, Is.EqualTo(new[] { (uint)mesh.FaceLineAttributes[mesh.Faces[original]] }));
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(decoded.OrderBy(text => text), Is.EqualTo(expected));
            Assert.That(notes.All, Is.Empty);
        });
    }

    private static string Canonical(int[] triangle)
    {
        var lowest = Array.IndexOf(triangle, triangle.Min());
        return $"{triangle[lowest]},{triangle[(lowest + 1) % 3]},{triangle[(lowest + 2) % 3]}";
    }

    private static double Distance(double[] point, double[] coordinates, int index) =>
        Math.Sqrt(Math.Pow(point[0] - coordinates[index * 3], 2) + Math.Pow(point[1] - coordinates[index * 3 + 1], 2) +
                  Math.Pow(point[2] - coordinates[index * 3 + 2], 2));

    private static PrcTestMesh Tetrahedron(double[] at, int faces)
    {
        var mesh = new PrcTestMesh();
        mesh.Points.Add([at[0], at[1], at[2]]);
        mesh.Points.Add([at[0] + 2, at[1], at[2]]);
        mesh.Points.Add([at[0], at[1] + 2, at[2]]);
        mesh.Points.Add([at[0], at[1], at[2] + 2]);
        mesh.AddTriangle(0, 2, 1, 0);
        mesh.AddTriangle(0, 1, 3, faces > 1 ? 1 : 0);
        mesh.AddTriangle(0, 3, 2, faces > 2 ? 2 : 0);
        mesh.AddTriangle(1, 2, 3, faces > 3 ? 3 : 0);
        mesh.CornerNormals = mesh.Triangles.Select(triangle => FlatNormals(mesh, triangle)).ToList();
        return mesh;
    }

    private static PrcTestMesh Strip(int columns, int rows, double step)
    {
        var mesh = new PrcTestMesh();
        for (var row = 0; row <= rows; row++)
        {
            for (var column = 0; column <= columns; column++)
            {
                mesh.Points.Add([column * step, row * step * 1.5, 0.3]);
            }
        }

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var corner = row * (columns + 1) + column;
                mesh.AddTriangle(corner, corner + 1, corner + columns + 2, 0);
                mesh.AddTriangle(corner, corner + columns + 2, corner + columns + 1, 0);
            }
        }

        mesh.CornerNormals = mesh.Triangles.Select(triangle => FlatNormals(mesh, triangle)).ToList();
        return mesh;
    }

    private static double[][] FlatNormals(PrcTestMesh mesh, int[] triangle)
    {
        var first = mesh.Points[triangle[0]];
        var second = mesh.Points[triangle[1]];
        var third = mesh.Points[triangle[2]];
        var normal = new PrcVector(second[0] - first[0], second[1] - first[1], second[2] - first[2])
            .Cross(new PrcVector(third[0] - first[0], third[1] - first[1], third[2] - first[2]));
        normal.TryUnitize(out normal);
        return [[normal.X, normal.Y, normal.Z], [normal.X, normal.Y, normal.Z], [normal.X, normal.Y, normal.Z]];
    }

    private static double[] SurfaceNormal(double[] point)
    {
        var normal = new PrcVector(-Math.Cos(point[0] * 0.4) * 0.8, Math.Sin(point[1] * 0.7) * 0.7, 1);
        normal.TryUnitize(out normal);
        return [normal.X, normal.Y, normal.Z];
    }
}
