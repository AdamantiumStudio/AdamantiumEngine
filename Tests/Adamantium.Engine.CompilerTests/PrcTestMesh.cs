using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Engine.CompilerTests;

/// <summary>A mesh to write as a PRC highly compressed tessellation: points, triangles wound counter-clockwise around
/// their normals, the face of each triangle and, when given, a normal for each corner, colors, texture coordinates,
/// planar faces and line attributes.</summary>
internal sealed class PrcTestMesh
{
    public List<double[]> Points { get; } = [];

    public List<int[]> Triangles { get; } = [];

    public List<int> Faces { get; } = [];

    /// <summary>Three unit normals a triangle, one a corner; null to have them recalculated.</summary>
    public List<double[][]> CornerNormals { get; set; }

    /// <summary>For each face, whether it is planar - one normal for all its triangles.</summary>
    public bool[] PlanarFaces { get; set; }

    /// <summary>For each face, its style plus one; null for none.</summary>
    public int[] FaceLineAttributes { get; set; }

    /// <summary>Red, green, blue and alpha of each corner; null for none.</summary>
    public List<byte[][]> CornerColors { get; set; }

    /// <summary>U and v of each corner; null for none.</summary>
    public List<float[][]> CornerTextures { get; set; }

    public int FaceCount => Faces.Count == 0 ? 0 : Faces.Max() + 1;

    public void AddTriangle(int first, int second, int third, int face)
    {
        Triangles.Add([first, second, third]);
        Faces.Add(face);
    }
}
