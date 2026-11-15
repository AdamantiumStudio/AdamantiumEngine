using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Rebuilds the mesh of a highly compressed tessellation (PRC Mesh Traversal) into a regular 3D
/// tessellation.</summary>
internal sealed class PrcCompressedMeshDecoder
{
    private const int RightNeighbor = 1;
    private const int LeftNeighbor = 2;
    private const int ColorCharacters = 5;
    private const int ShownOnly = PrcGraphics.Show;

    private readonly PrcCompressedTessellationData _data;
    private readonly PrcEntityReader _reader;
    private readonly List<PrcVector> _points = [];
    private readonly Stack<PrcGateEdge> _gates = new();
    private readonly List<PrcDecodedTriangle> _triangles = [];
    private readonly Dictionary<long, int> _edgeTriangles = new();
    private readonly PrcCompressedNormals _normals;
    private int _pointCursor;
    private int _flagCursor;
    private int _referenceCursor;

    private PrcCompressedMeshDecoder(PrcCompressedTessellationData data, PrcEntityReader reader)
    {
        _data = data;
        _reader = reader;
        _normals = data.MustRecalculateNormals ? null : new PrcCompressedNormals(data, reader.Bits);
    }

    /// <summary>The regular 3D tessellation <paramref name="data"/> holds.</summary>
    internal static Prc3DTessellation Decode(PrcCompressedTessellationData data, PrcEntityReader reader)
    {
        var decoder = new PrcCompressedMeshDecoder(data, reader);
        decoder.Traverse();
        return decoder.Build();
    }

    private void Traverse()
    {
        var triangleCount = _data.TriangleFaces.Length;
        if (_data.EdgeStatus.Length < triangleCount)
        {
            throw _reader.Bits.CreateCorruptException($"a compressed tessellation has {_data.EdgeStatus.Length} edge statuses for its {triangleCount} triangles");
        }

        while (_triangles.Count < triangleCount)
        {
            while (_gates.Count > 0 && IsClosed(_gates.Peek()))
            {
                _gates.Pop();
            }

            var triangle = _gates.Count == 0 ? StartTraversal() : GrowAcross(_gates.Pop());
            CountEdges(triangle);
            triangle.Face = _data.TriangleFaces[_triangles.Count];
            if (triangle.Face < 0)
            {
                throw _reader.Bits.CreateCorruptException($"triangle {_triangles.Count} of a compressed tessellation lies in face {triangle.Face}");
            }

            _normals?.Restore(triangle, _points);
            triangle.IsReversed = IsReversed(triangle);
            PushNeighbors(triangle, _data.EdgeStatus[_triangles.Count]);
            _triangles.Add(triangle);
        }

        if (_pointCursor * 3 != _data.Points.Length || _flagCursor != _data.PointIsReference.Length ||
            _referenceCursor != _data.PointReferences.Length || (_normals != null && !_normals.IsAtEnd))
        {
            _reader.Tell("a compressed tessellation stores more points, references or normals than its triangles use; the rest are left out");
        }
    }

    private bool IsClosed(PrcGateEdge gate) => _edgeTriangles.TryGetValue(GetEdgeKey(gate.Lower, gate.Higher), out var count) && count > 1;

    private void CountEdges(PrcDecodedTriangle triangle)
    {
        long[] keys =
        [
            GetEdgeKey(triangle.First, triangle.Second), GetEdgeKey(triangle.Second, triangle.Third), GetEdgeKey(triangle.Third, triangle.First)
        ];
        foreach (var key in keys)
        {
            _edgeTriangles[key] = _edgeTriangles.GetValueOrDefault(key) + 1;
        }
    }

    private static long GetEdgeKey(int first, int second) => ((long)Math.Min(first, second) << 32) | (uint)Math.Max(first, second);

    private PrcDecodedTriangle StartTraversal()
    {
        var first = ReadVertex(() => GetOrigin() + ReadOffset());
        var second = ReadVertex(() => _points[first] + ReadOffset());
        var third = ReadVertex(() => (_points[first] + _points[second]) * 0.5 + ReadOffset());
        return first > second ? new PrcDecodedTriangle(second, first, third) : new PrcDecodedTriangle(first, second, third);
    }

    private PrcDecodedTriangle GrowAcross(PrcGateEdge gate)
    {
        var apex = ReadVertex(() => PlaceApex(gate));
        return new PrcDecodedTriangle(gate.Lower, gate.Higher, apex);
    }

    private int ReadVertex(Func<PrcVector> place)
    {
        if (_flagCursor >= _data.PointIsReference.Length)
        {
            throw _reader.Bits.CreateCorruptException("a compressed tessellation runs out of point reference flags before its triangles end");
        }

        if (_data.PointIsReference[_flagCursor++])
        {
            if (_referenceCursor >= _data.PointReferences.Length)
            {
                throw _reader.Bits.CreateCorruptException("a compressed tessellation runs out of point references before its triangles end");
            }

            var reference = _data.PointReferences[_referenceCursor++];
            if (reference < 0 || reference >= _points.Count)
            {
                throw _reader.Bits.CreateCorruptException($"a compressed tessellation refers to point {reference} of the {_points.Count} met so far");
            }

            return reference;
        }

        _points.Add(place());
        return _points.Count - 1;
    }

    private PrcVector GetOrigin() => new(_data.Origin.X, _data.Origin.Y, _data.Origin.Z);

    private PrcVector ReadOffset()
    {
        if (_pointCursor * 3 + 3 > _data.Points.Length)
        {
            throw _reader.Bits.CreateCorruptException("a compressed tessellation runs out of point coordinates before its triangles end");
        }

        var at = _pointCursor++ * 3;
        return new PrcVector(_data.Points[at] * _data.Tolerance, _data.Points[at + 1] * _data.Tolerance, _data.Points[at + 2] * _data.Tolerance);
    }

    private PrcVector PlaceApex(PrcGateEdge gate)
    {
        var lower = _points[gate.Lower];
        var higher = _points[gate.Higher];
        var far = _points[gate.Far];
        var origin = (lower + higher) * 0.5;
        if (!(lower - higher).TryNormalize(out var xAxis))
        {
            throw _reader.Bits.CreateCorruptException("a compressed tessellation grows a triangle across an edge of no length");
        }

        if (!(far - origin).Cross(xAxis).TryNormalize(out var zAxis) && !xAxis.TryGetOrthogonal(out zAxis))
        {
            throw _reader.Bits.CreateCorruptException("a compressed tessellation grows a triangle across an edge it cannot build axes on");
        }

        zAxis.Cross(xAxis).TryNormalize(out var yAxis);
        if (yAxis.Dot(origin - far) > 0)
        {
            yAxis = -yAxis;
            zAxis = -zAxis;
        }

        var offset = ReadOffset();
        return origin + xAxis * offset.X + yAxis * offset.Y + zAxis * offset.Z;
    }

    private bool IsReversed(PrcDecodedTriangle triangle)
    {
        if (_data.MustRecalculateNormals)
        {
            var index = _triangles.Count;
            return index < _data.NormalIsReversed.Length && _data.NormalIsReversed[index];
        }

        var first = _points[triangle.First];
        var second = _points[triangle.Second];
        var third = _points[triangle.Third];
        var normal = _normals.GetNormal(triangle.FirstNormal);
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
            _gates.Push(right);
        }

        if ((status & LeftNeighbor) != 0)
        {
            _gates.Push(left);
        }
    }

    private Prc3DTessellation Build()
    {
        var tessellation = new Prc3DTessellation
        {
            Type = PrcTypes.Tessellation3DCompressed,
            HasFaces = _data.HasFaces,
            MustRecalculateNormals = _data.MustRecalculateNormals,
            CreaseAngle = _data.CreaseAngle
        };
        tessellation.Coordinates = Flatten(_points);
        if (_normals != null)
        {
            tessellation.Normals = Flatten(_normals.All);
        }

        var lineAttributes = AssignLineAttributes();
        var colors = AssignColors();
        var textures = AssignTextures(tessellation);
        if (_data.Behaviors.Any(behavior => behavior != ShownOnly))
        {
            _reader.Tell("graphics behaviors other than shown on the faces of compressed tessellations are left out; the faces are drawn");
        }

        List<uint> indices = [];
        foreach (var face in Enumerable.Range(0, _triangles.Count).GroupBy(index => _triangles[index].Face).OrderBy(group => group.Key))
        {
            List<int> run = [];
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
            var triangle = _triangles[index];
            foreach (var corner in GetWoundCorners(triangle))
            {
                if (!_data.MustRecalculateNormals)
                {
                    indices.Add((uint)GetNormal(triangle, corner) * 3);
                }

                if (textured)
                {
                    indices.Add((uint)textures[index * 3 + corner] * 2);
                }

                indices.Add((uint)GetPoint(triangle, corner) * 3);
                if (colored)
                {
                    face.VertexColors.Add(colors[index * 3 + corner] ?? default);
                }
            }
        }

        return face;
    }

    private static int[] GetWoundCorners(PrcDecodedTriangle triangle) => triangle.IsReversed ? [0, 1, 2] : [0, 2, 1];

    private static int GetPoint(PrcDecodedTriangle triangle, int corner) =>
        corner == 0 ? triangle.First : corner == 1 ? triangle.Second : triangle.Third;

    private static int GetNormal(PrcDecodedTriangle triangle, int corner) =>
        corner == 0 ? triangle.FirstNormal : corner == 1 ? triangle.SecondNormal : triangle.ThirdNormal;

    private bool IsFlagSetOnFace(bool[] flags, int face) =>
        flags != null && flags.Length > 0 && (_data.HasFaces ? face < flags.Length && flags[face] : flags[0]);

    private int[] AssignLineAttributes()
    {
        var assigned = new int[_triangles.Count];
        var ofFace = new Dictionary<int, int>();
        var cursor = 0;
        for (var index = 0; index < _triangles.Count; index++)
        {
            var face = _triangles[index].Face;
            if (!IsFlagSetOnFace(_data.IsMultipleLineAttributeOnFace, face) && ofFace.TryGetValue(face, out var attribute))
            {
                assigned[index] = attribute;
                continue;
            }

            if (cursor >= _data.LineAttributes.Length)
            {
                if (_data.LineAttributes.Length > 0)
                {
                    _reader.Tell("a compressed tessellation has fewer line attributes than its faces; the rest are drawn by their item's style");
                }

                continue;
            }

            assigned[index] = _data.LineAttributes[cursor++];
            ofFace.TryAdd(face, assigned[index]);
        }

        return assigned;
    }

    private Color?[] AssignColors()
    {
        if (_data.IsPointColorOnFace == null)
        {
            return null;
        }

        var colors = new Color?[_triangles.Count * 3];
        var cursor = 0;
        for (var index = 0; index < _triangles.Count; index++)
        {
            if (!IsFlagSetOnFace(_data.IsPointColorOnFace, _triangles[index].Face))
            {
                continue;
            }

            for (var corner = 0; corner < 3; corner++)
            {
                if (cursor + ColorCharacters > _data.PointColors.Length)
                {
                    _reader.Tell("a compressed tessellation has fewer point colors than its colored faces' vertices; they are left out");
                    return null;
                }

                var hasAlpha = _data.PointColors[cursor] != 0;
                colors[index * 3 + corner] = new Color((byte)_data.PointColors[cursor + 1], (byte)_data.PointColors[cursor + 2],
                    (byte)_data.PointColors[cursor + 3], hasAlpha ? (byte)_data.PointColors[cursor + 4] : byte.MaxValue);
                cursor += ColorCharacters;
            }
        }

        return colors;
    }

    private int[] AssignTextures(Prc3DTessellation tessellation)
    {
        var texture = _data.Texture;
        if (texture == null)
        {
            return null;
        }

        var assigned = new int[_triangles.Count * 3];
        var stored = new Dictionary<int, List<int>>();
        var flagCursor = 0;
        var referenceCursor = 0;
        var parameterCursor = 0;
        for (var index = 0; index < _triangles.Count; index++)
        {
            var triangle = _triangles[index];
            var textured = IsFlagSetOnFace(_data.FaceHasTexture, triangle.Face);
            for (var corner = 0; corner < 3; corner++)
            {
                assigned[index * 3 + corner] = -1;
                if (!textured)
                {
                    continue;
                }

                if (flagCursor >= texture.ReferenceFlags.Length)
                {
                    _reader.Tell("a compressed tessellation has fewer texture coordinates than its textured faces' vertices; they are left out");
                    return null;
                }

                var point = GetPoint(triangle, corner);
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
                        _reader.Tell("a compressed tessellation refers to texture coordinates its vertex does not have; they are left out");
                        return null;
                    }

                    assigned[index * 3 + corner] = ofPoint[reference];
                    continue;
                }

                if (parameterCursor + 2 > texture.Parameters.Length)
                {
                    _reader.Tell("a compressed tessellation has fewer texture coordinates than its textured faces' vertices; they are left out");
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
            _reader.Tell("a compressed tessellation stores more texture coordinates than its textured faces use; the rest are left out");
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
