using System;
using System.IO;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Walks the triangulated data of one face of a 3D tessellation: for each kind of triangulation its flags
/// name, lowest bit first, the counts of its sizes and the normal, texture and point indices they cover.</summary>
internal sealed class PrcFaceCursor
{
    private readonly Prc3DTessellation _tessellation;
    private readonly PrcFaceTessellation _face;
    private readonly PrcMeshData _mesh;
    private readonly ImportNotes _notes;
    private readonly int _textureIndexes;
    private long _position;
    private int _sizeAt;
    private int _colorAt;

    /// <summary>A cursor at <paramref name="start"/> of the triangulated indices: the face's own start, or - when the
    /// tessellation stores no normals, and its faces' starts still count them - where the previous face ended.</summary>
    internal PrcFaceCursor(Prc3DTessellation tessellation, PrcFaceTessellation face, PrcMeshData mesh, ImportNotes notes, long start)
    {
        _tessellation = tessellation;
        _face = face;
        _mesh = mesh;
        _notes = notes;
        _textureIndexes = (int)Math.Min(face.TextureCoordinateIndexes, 64);
        _position = start;
        if (face.VertexColors != null)
        {
            mesh.HasColors = true;
        }
    }

    /// <summary>The triangulated index after the last one read.</summary>
    internal long Position => _position;

    internal void Read(ref long corner)
    {
        for (var bit = 0; bit < 32; bit++)
        {
            var kind = 1u << bit;
            if ((_face.UsedEntities & kind) == 0)
            {
                continue;
            }

            var textured = kind is PrcTessellationReader.TriangleTextured or PrcTessellationReader.TriangleFanTextured
                or PrcTessellationReader.TriangleStripeTextured or PrcTessellationReader.TriangleOneNormalTextured
                or PrcTessellationReader.TriangleFanOneNormalTextured or PrcTessellationReader.TriangleStripeOneNormalTextured;
            var textures = textured ? _textureIndexes : 0;
            switch (kind)
            {
                case PrcTessellationReader.Triangle or PrcTessellationReader.TriangleTextured:
                    var triangles = ReadSize();
                    for (var triangle = 0u; triangle < triangles; triangle++)
                    {
                        AddRun(3, false, false, textures, ref corner);
                    }

                    break;
                case PrcTessellationReader.TriangleOneNormal or PrcTessellationReader.TriangleOneNormalTextured:
                    var flat = ReadSize();
                    for (var triangle = 0u; triangle < flat; triangle++)
                    {
                        AddRun(3, true, false, textures, ref corner);
                    }

                    break;
                case PrcTessellationReader.TriangleFan or PrcTessellationReader.TriangleFanTextured
                    or PrcTessellationReader.TriangleStripe or PrcTessellationReader.TriangleStripeTextured:
                    var runs = ReadSize();
                    for (var run = 0u; run < runs; run++)
                    {
                        AddRun(ReadSize(), false, kind is PrcTessellationReader.TriangleStripe or PrcTessellationReader.TriangleStripeTextured,
                            textures, ref corner);
                    }

                    break;
                case PrcTessellationReader.TriangleFanOneNormal or PrcTessellationReader.TriangleFanOneNormalTextured
                    or PrcTessellationReader.TriangleStripeOneNormal or PrcTessellationReader.TriangleStripeOneNormalTextured:
                    var oneNormalRuns = ReadSize();
                    for (var run = 0u; run < oneNormalRuns; run++)
                    {
                        var size = ReadSize();
                        AddRun(size & ~PrcTessellationReader.SingleNormal, (size & PrcTessellationReader.SingleNormal) != 0,
                            kind is PrcTessellationReader.TriangleStripeOneNormal or PrcTessellationReader.TriangleStripeOneNormalTextured,
                            textures, ref corner);
                    }

                    break;
                default:
                    _notes.Tell($"face tessellations of kind 0x{kind:X}, which the specification marks unused, are left out");
                    return;
            }
        }
    }

    private uint ReadSize()
    {
        if (_sizeAt >= _face.SizesTriangulated.Length)
        {
            throw new InvalidDataException("A PRC face tessellation has fewer counts than its kinds of triangulation need");
        }

        return _face.SizesTriangulated[_sizeAt++];
    }

    private void AddRun(uint count, bool singleNormal, bool stripe, int textures, ref long corner)
    {
        var indices = _tessellation.TriangulatedIndices;
        var perVertex = (singleNormal || _tessellation.MustRecalculateNormals ? 0 : 1) + textures + 1;
        var needed = (decimal)count * perVertex + (singleNormal && !_tessellation.MustRecalculateNormals ? 1 : 0);
        if (needed > indices.Length - _position)
        {
            throw new InvalidDataException($"A PRC face tessellation needs {needed} triangulated indices where {indices.Length - _position} are left");
        }

        long sharedNormal = -1;
        if (singleNormal && !_tessellation.MustRecalculateNormals)
        {
            sharedNormal = indices[_position++];
        }

        var vertices = new PrcRunVertex[count];
        for (var vertex = 0; vertex < count; vertex++)
        {
            var normal = sharedNormal;
            if (!singleNormal && !_tessellation.MustRecalculateNormals)
            {
                normal = indices[_position++];
            }

            long texture = -1;
            for (var layer = 0; layer < textures; layer++)
            {
                var index = indices[_position++];
                if (layer == 0)
                {
                    texture = index;
                }
            }

            vertices[vertex] = new PrcRunVertex(indices[_position++], normal, texture, ReadColor());
        }

        if (count < 3)
        {
            return;
        }

        if (!stripe && count == 3)
        {
            AddTriangle(vertices[0], vertices[1], vertices[2], ref corner);
            return;
        }

        for (var triangle = 0; triangle + 2 < count; triangle++)
        {
            if (!stripe)
            {
                AddTriangle(vertices[0], vertices[triangle + 1], vertices[triangle + 2], ref corner);
            }
            else if (triangle % 2 == 0)
            {
                AddTriangle(vertices[triangle], vertices[triangle + 1], vertices[triangle + 2], ref corner);
            }
            else
            {
                AddTriangle(vertices[triangle + 1], vertices[triangle], vertices[triangle + 2], ref corner);
            }
        }
    }

    private Color ReadColor()
    {
        if (_face.VertexColors == null)
        {
            return default;
        }

        return _colorAt < _face.VertexColors.Count ? _face.VertexColors[_colorAt++] : _face.VertexColors[^1];
    }

    private void AddTriangle(PrcRunVertex first, PrcRunVertex second, PrcRunVertex third, ref long corner)
    {
        PrcRunVertex[] vertices = [first, second, third];
        foreach (var vertex in vertices)
        {
            PrcMeshBuilder.CheckPoint(_tessellation.Coordinates, vertex.Point, "a 3D tessellation");
            var point = new Vector3<double>(_tessellation.Coordinates[vertex.Point], _tessellation.Coordinates[vertex.Point + 1],
                _tessellation.Coordinates[vertex.Point + 2]);
            var normal = Vector3F.Zero;
            if (_mesh.HasNormals)
            {
                PrcMeshBuilder.CheckPoint(_tessellation.Normals, vertex.Normal, "a 3D tessellation's normals");
                normal = new Vector3F((float)_tessellation.Normals[vertex.Normal], (float)_tessellation.Normals[vertex.Normal + 1],
                    (float)_tessellation.Normals[vertex.Normal + 2]);
            }

            var texture = Vector2F.Zero;
            if (vertex.Texture >= 0)
            {
                if (vertex.Texture + 1 >= _tessellation.TextureCoordinates.Length)
                {
                    throw new InvalidDataException($"A PRC texture index points at coordinate {vertex.Texture} of {_tessellation.TextureCoordinates.Length}");
                }

                texture = new Vector2F((float)_tessellation.TextureCoordinates[vertex.Texture], (float)_tessellation.TextureCoordinates[vertex.Texture + 1]);
            }

            var key = new PrcVertexKey(vertex.Point, vertex.Normal, vertex.Texture, _face.VertexColors != null ? corner : -1);
            corner++;
            _mesh.AddCorner(key, point, normal, vertex.Color, texture);
        }
    }
}
