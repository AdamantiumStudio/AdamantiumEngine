using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Rebuilds the mesh of a highly compressed tessellation (PRC_TYPE_TESS_3D_Compressed, Mesh Traversal) into
/// a regular 3D tessellation. Triangles are reached across edges kept on a last-in-first-out stack; when it is empty
/// a new traversal starts on a triangle of three vertices, else the edge on top is popped - one with triangles on both
/// sides by now is passed over - and the triangle across it takes one. Each vertex is a point met before, by its
/// index, or a new one: three integers, times the tolerance, along axes built from the edge it is reached across and
/// the far vertex of the triangle that pushed that edge - or, for a starting triangle, from the origin, the first
/// vertex and the middle of the first two. A triangle's normals are read as it is built; whether it is reversed - by
/// its stored flag, or its normal against its traversal order - decides which of its two other edges is its left and
/// which its right neighbour, the right pushed before the left, and its winding: as traversed when reversed, else
/// with its last two vertices exchanged. Traversals follow one another until every triangle is built.</summary>
internal sealed class PrcCompressedMeshDecoder
{
    private const int RightNeighbor = 1;
    private const int LeftNeighbor = 2;
    private const int ColorCharacters = 5;
    private const int ShownOnly = PrcGraphics.Show;

    private readonly PrcCompressedTessellationData data;
    private readonly PrcEntityReader reader;
    private readonly List<PrcVector> points = [];
    private readonly Stack<PrcGateEdge> gates = new();
    private readonly List<PrcDecodedTriangle> triangles = [];
    private readonly Dictionary<long, int> edgeTriangles = new();
    private readonly PrcCompressedNormals normals;
    private int pointCursor;
    private int flagCursor;
    private int referenceCursor;

    private PrcCompressedMeshDecoder(PrcCompressedTessellationData data, PrcEntityReader reader)
    {
        this.data = data;
        this.reader = reader;
        normals = data.MustRecalculateNormals ? null : new PrcCompressedNormals(data, reader.Bits);
    }

    /// <summary>The regular 3D tessellation <paramref name="data"/> holds: its points, its normals unless they are to
    /// be recalculated, its texture coordinates, and one face a face of the file - one a run of triangles of the same
    /// line attribute in a face of several.</summary>
    internal static Prc3DTessellation Decode(PrcCompressedTessellationData data, PrcEntityReader reader)
    {
        var decoder = new PrcCompressedMeshDecoder(data, reader);
        decoder.Traverse();
        return decoder.Build();
    }

    private void Traverse()
    {
        var triangleCount = data.TriangleFaces.Length;
        if (data.EdgeStatus.Length < triangleCount)
        {
            throw reader.Bits.Corrupt($"a compressed tessellation has {data.EdgeStatus.Length} edge statuses for its {triangleCount} triangles");
        }

        while (triangles.Count < triangleCount)
        {
            while (gates.Count > 0 && IsClosed(gates.Peek()))
            {
                gates.Pop();
            }

            var triangle = gates.Count == 0 ? StartTraversal() : GrowAcross(gates.Pop());
            CountEdges(triangle);
            triangle.Face = data.TriangleFaces[triangles.Count];
            if (triangle.Face < 0)
            {
                throw reader.Bits.Corrupt($"triangle {triangles.Count} of a compressed tessellation lies in face {triangle.Face}");
            }

            normals?.Restore(triangle, points);
            triangle.IsReversed = IsReversed(triangle);
            PushNeighbors(triangle, data.EdgeStatus[triangles.Count]);
            triangles.Add(triangle);
        }

        if (pointCursor * 3 != data.Points.Length || flagCursor != data.PointIsReference.Length ||
            referenceCursor != data.PointReferences.Length || (normals != null && !normals.IsAtEnd))
        {
            reader.Tell("a compressed tessellation stores more points, references or normals than its triangles use; the rest are left out");
        }
    }

    private bool IsClosed(PrcGateEdge gate) => edgeTriangles.TryGetValue(EdgeKey(gate.Lower, gate.Higher), out var count) && count > 1;

    private void CountEdges(PrcDecodedTriangle triangle)
    {
        foreach (var key in new[]
                 {
                     EdgeKey(triangle.First, triangle.Second), EdgeKey(triangle.Second, triangle.Third), EdgeKey(triangle.Third, triangle.First)
                 })
        {
            edgeTriangles[key] = edgeTriangles.GetValueOrDefault(key) + 1;
        }
    }

    private static long EdgeKey(int first, int second) => ((long)Math.Min(first, second) << 32) | (uint)Math.Max(first, second);

    private PrcDecodedTriangle StartTraversal()
    {
        var first = NextVertex(() => Origin() + NextOffset());
        var second = NextVertex(() => points[first] + NextOffset());
        var third = NextVertex(() => (points[first] + points[second]) * 0.5 + NextOffset());
        return first > second ? new PrcDecodedTriangle(second, first, third) : new PrcDecodedTriangle(first, second, third);
    }

    private PrcDecodedTriangle GrowAcross(PrcGateEdge gate)
    {
        var apex = NextVertex(() => Apex(gate));
        return new PrcDecodedTriangle(gate.Lower, gate.Higher, apex);
    }

    private int NextVertex(Func<PrcVector> place)
    {
        if (flagCursor >= data.PointIsReference.Length)
        {
            throw reader.Bits.Corrupt("a compressed tessellation runs out of point reference flags before its triangles end");
        }

        if (data.PointIsReference[flagCursor++])
        {
            if (referenceCursor >= data.PointReferences.Length)
            {
                throw reader.Bits.Corrupt("a compressed tessellation runs out of point references before its triangles end");
            }

            var reference = data.PointReferences[referenceCursor++];
            if (reference < 0 || reference >= points.Count)
            {
                throw reader.Bits.Corrupt($"a compressed tessellation refers to point {reference} of the {points.Count} met so far");
            }

            return reference;
        }

        points.Add(place());
        return points.Count - 1;
    }

    private PrcVector Origin() => new(data.Origin.X, data.Origin.Y, data.Origin.Z);

    private PrcVector NextOffset()
    {
        if (pointCursor * 3 + 3 > data.Points.Length)
        {
            throw reader.Bits.Corrupt("a compressed tessellation runs out of point coordinates before its triangles end");
        }

        var at = pointCursor++ * 3;
        return new PrcVector(data.Points[at] * data.Tolerance, data.Points[at + 1] * data.Tolerance, data.Points[at + 2] * data.Tolerance);
    }

    private PrcVector Apex(PrcGateEdge gate)
    {
        var lower = points[gate.Lower];
        var higher = points[gate.Higher];
        var far = points[gate.Far];
        var origin = (lower + higher) * 0.5;
        if (!(lower - higher).TryUnitize(out var xAxis))
        {
            throw reader.Bits.Corrupt("a compressed tessellation grows a triangle across an edge of no length");
        }

        if (!(far - origin).Cross(xAxis).TryUnitize(out var zAxis) && !xAxis.TryOrthogonal(out zAxis))
        {
            throw reader.Bits.Corrupt("a compressed tessellation grows a triangle across an edge it cannot build axes on");
        }

        zAxis.Cross(xAxis).TryUnitize(out var yAxis);
        if (yAxis.Dot(origin - far) > 0)
        {
            yAxis = -yAxis;
            zAxis = -zAxis;
        }

        var offset = NextOffset();
        return origin + xAxis * offset.X + yAxis * offset.Y + zAxis * offset.Z;
    }

    private bool IsReversed(PrcDecodedTriangle triangle)
    {
        if (data.MustRecalculateNormals)
        {
            var index = triangles.Count;
            return index < data.NormalIsReversed.Length && data.NormalIsReversed[index];
        }

        var first = points[triangle.First];
        var second = points[triangle.Second];
        var third = points[triangle.Third];
        var normal = normals.NormalAt(triangle.FirstNormal);
        return (third - (first + second) * 0.5).Cross(second - first).Dot(normal) < 0;
    }

    private void PushNeighbors(PrcDecodedTriangle triangle, int status)
    {
        var acrossFirst = new PrcGateEdge(triangle.First, triangle.Third, triangle.Second);
        var acrossSecond = new PrcGateEdge(triangle.Second, triangle.Third, triangle.First);
        var left = triangle.IsReversed ? acrossSecond : acrossFirst;
        var right = triangle.IsReversed ? acrossFirst : acrossSecond;

        if ((status & RightNeighbor) != 0)
        {
            gates.Push(right);
        }

        if ((status & LeftNeighbor) != 0)
        {
            gates.Push(left);
        }
    }

    private Prc3DTessellation Build()
    {
        var tessellation = new Prc3DTessellation
        {
            Type = PrcTypes.Tessellation3DCompressed,
            HasFaces = data.HasFaces,
            MustRecalculateNormals = data.MustRecalculateNormals,
            CreaseAngle = data.CreaseAngle
        };
        tessellation.Coordinates = Flatten(points);
        if (normals != null)
        {
            tessellation.Normals = Flatten(normals.All);
        }

        var lineAttributes = AssignLineAttributes();
        var colors = AssignColors();
        var textures = AssignTextures(tessellation);
        if (data.Behaviors.Any(behavior => behavior != ShownOnly))
        {
            reader.Tell("graphics behaviours other than shown on the faces of compressed tessellations are left out; the faces are drawn");
        }

        var indices = new List<uint>();
        foreach (var face in Enumerable.Range(0, triangles.Count).GroupBy(index => triangles[index].Face).OrderBy(group => group.Key))
        {
            var run = new List<int>();
            foreach (var index in face)
            {
                if (run.Count > 0 && lineAttributes[run[0]] != lineAttributes[index])
                {
                    tessellation.Faces.Add(BuildFace(run, lineAttributes, colors, textures, indices));
                    run.Clear();
                }

                run.Add(index);
            }

            tessellation.Faces.Add(BuildFace(run, lineAttributes, colors, textures, indices));
        }

        tessellation.TriangulatedIndices = indices.ToArray();
        return tessellation;
    }

    private PrcFaceTessellation BuildFace(List<int> run, int[] lineAttributes, Color?[] colors, int[] textures, List<uint> indices)
    {
        var textured = textures != null && textures[run[0] * 3] >= 0;
        var face = new PrcFaceTessellation
        {
            UsedEntities = textured ? PrcTessellationReader.TriangleTextured : PrcTessellationReader.Triangle,
            StartTriangulated = (uint)indices.Count,
            SizesTriangulated = [(uint)run.Count],
            TextureCoordinateIndexes = textured ? 1u : 0u,
            LineAttributes = lineAttributes[run[0]] > 0 ? [(uint)lineAttributes[run[0]]] : []
        };
        var colored = colors != null && colors[run[0] * 3].HasValue;
        if (colored)
        {
            face.VertexColors = [];
        }

        foreach (var index in run)
        {
            var triangle = triangles[index];
            foreach (var corner in WoundCorners(triangle))
            {
                if (!data.MustRecalculateNormals)
                {
                    indices.Add((uint)NormalOf(triangle, corner) * 3);
                }

                if (textured)
                {
                    indices.Add((uint)textures[index * 3 + corner] * 2);
                }

                indices.Add((uint)PointOf(triangle, corner) * 3);
                if (colored)
                {
                    face.VertexColors.Add(colors[index * 3 + corner] ?? default);
                }
            }
        }

        return face;
    }

    private static int[] WoundCorners(PrcDecodedTriangle triangle) => triangle.IsReversed ? [0, 1, 2] : [0, 2, 1];

    private static int PointOf(PrcDecodedTriangle triangle, int corner) =>
        corner == 0 ? triangle.First : corner == 1 ? triangle.Second : triangle.Third;

    private static int NormalOf(PrcDecodedTriangle triangle, int corner) =>
        corner == 0 ? triangle.FirstNormal : corner == 1 ? triangle.SecondNormal : triangle.ThirdNormal;

    private bool FlagOfFace(bool[] flags, int face) =>
        flags != null && flags.Length > 0 && (data.HasFaces ? face < flags.Length && flags[face] : flags[0]);

    private int[] AssignLineAttributes()
    {
        var assigned = new int[triangles.Count];
        var ofFace = new Dictionary<int, int>();
        var cursor = 0;
        for (var index = 0; index < triangles.Count; index++)
        {
            var face = triangles[index].Face;
            if (!FlagOfFace(data.IsMultipleLineAttributeOnFace, face) && ofFace.TryGetValue(face, out var attribute))
            {
                assigned[index] = attribute;
                continue;
            }

            if (cursor >= data.LineAttributes.Length)
            {
                if (data.LineAttributes.Length > 0)
                {
                    reader.Tell("a compressed tessellation has fewer line attributes than its faces; the rest are drawn by their item's style");
                }

                continue;
            }

            assigned[index] = data.LineAttributes[cursor++];
            ofFace.TryAdd(face, assigned[index]);
        }

        return assigned;
    }

    private Color?[] AssignColors()
    {
        if (data.IsPointColorOnFace == null)
        {
            return null;
        }

        var colors = new Color?[triangles.Count * 3];
        var cursor = 0;
        for (var index = 0; index < triangles.Count; index++)
        {
            if (!FlagOfFace(data.IsPointColorOnFace, triangles[index].Face))
            {
                continue;
            }

            for (var corner = 0; corner < 3; corner++)
            {
                if (cursor + ColorCharacters > data.PointColors.Length)
                {
                    reader.Tell("a compressed tessellation has fewer point colours than its coloured faces' vertices; they are left out");
                    return null;
                }

                var hasAlpha = data.PointColors[cursor] != 0;
                colors[index * 3 + corner] = new Color((byte)data.PointColors[cursor + 1], (byte)data.PointColors[cursor + 2],
                    (byte)data.PointColors[cursor + 3], hasAlpha ? (byte)data.PointColors[cursor + 4] : byte.MaxValue);
                cursor += ColorCharacters;
            }
        }

        return colors;
    }

    private int[] AssignTextures(Prc3DTessellation tessellation)
    {
        var texture = data.Texture;
        if (texture == null)
        {
            return null;
        }

        var assigned = new int[triangles.Count * 3];
        var stored = new Dictionary<int, List<int>>();
        var flagCursor = 0;
        var referenceCursor = 0;
        var parameterCursor = 0;
        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            var textured = FlagOfFace(data.FaceHasTexture, triangle.Face);
            for (var corner = 0; corner < 3; corner++)
            {
                assigned[index * 3 + corner] = -1;
                if (!textured)
                {
                    continue;
                }

                if (flagCursor >= texture.ReferenceFlags.Length)
                {
                    reader.Tell("a compressed tessellation has fewer texture coordinates than its textured faces' vertices; they are left out");
                    return null;
                }

                var point = PointOf(triangle, corner);
                if (!stored.TryGetValue(point, out var ofPoint))
                {
                    ofPoint = [];
                    stored.Add(point, ofPoint);
                }

                if (texture.ReferenceFlags[flagCursor++])
                {
                    var reference = referenceCursor < texture.References.Length ? texture.References[referenceCursor++] : -1;
                    if (reference < 0 || reference >= ofPoint.Count)
                    {
                        reader.Tell("a compressed tessellation refers to texture coordinates its vertex does not have; they are left out");
                        return null;
                    }

                    assigned[index * 3 + corner] = ofPoint[reference];
                    continue;
                }

                if (parameterCursor + 2 > texture.Parameters.Length)
                {
                    reader.Tell("a compressed tessellation has fewer texture coordinates than its textured faces' vertices; they are left out");
                    return null;
                }

                assigned[index * 3 + corner] = parameterCursor / 2;
                ofPoint.Add(parameterCursor / 2);
                parameterCursor += 2;
            }
        }

        if (flagCursor != texture.ReferenceFlags.Length || referenceCursor != texture.References.Length ||
            parameterCursor != texture.Parameters.Length)
        {
            reader.Tell("a compressed tessellation stores more texture coordinates than its textured faces use; the rest are left out");
        }

        tessellation.TextureCoordinates = texture.Parameters.Select(parameter => (double)parameter).ToArray();
        return assigned;
    }

    private static double[] Flatten(IReadOnlyList<PrcVector> vectors)
    {
        var values = new double[vectors.Count * 3];
        for (var index = 0; index < vectors.Count; index++)
        {
            values[index * 3] = vectors[index].X;
            values[index * 3 + 1] = vectors[index].Y;
            values[index * 3 + 2] = vectors[index].Z;
        }

        return values;
    }
}

/// <summary>An edge a triangle is reached across: its two vertices, the earlier met first, and the third vertex of
/// the triangle that pushed it.</summary>
internal readonly struct PrcGateEdge
{
    internal PrcGateEdge(int first, int second, int far)
    {
        Lower = Math.Min(first, second);
        Higher = Math.Max(first, second);
        Far = far;
    }

    internal int Lower { get; }

    internal int Higher { get; }

    internal int Far { get; }
}

/// <summary>A triangle as the traversal builds it: its vertices in traversal order, the normal of each, its face and
/// whether it is reversed.</summary>
internal sealed class PrcDecodedTriangle
{
    internal PrcDecodedTriangle(int first, int second, int third)
    {
        First = first;
        Second = second;
        Third = third;
    }

    internal int First { get; }

    internal int Second { get; }

    internal int Third { get; }

    internal int FirstNormal { get; set; }

    internal int SecondNormal { get; set; }

    internal int ThirdNormal { get; set; }

    internal int Face { get; set; }

    internal bool IsReversed { get; set; }
}
