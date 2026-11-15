using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Points, normals, colours and indices of one mesh being built from PRC tessellation.</summary>
internal sealed class PrcMeshData
{
    private readonly Dictionary<PrcVertexKey, int> vertexOf = new();

    internal PrcMeshData(PrimitiveType topology)
    {
        Topology = topology;
    }

    internal PrimitiveType Topology { get; }

    internal List<Vector3<double>> Points { get; } = [];

    internal List<Vector3F> Normals { get; } = [];

    internal List<Color> Colors { get; } = [];

    internal List<Vector2F> TextureCoordinates { get; } = [];

    internal List<int> Indices { get; } = [];

    internal bool HasNormals { get; set; } = true;

    internal bool HasColors { get; set; }

    internal bool HasTextureCoordinates { get; set; }

    /// <summary>Adds the vertex <paramref name="key"/> names, once however many corners share it, and its index.</summary>
    internal void AddCorner(PrcVertexKey key, Vector3<double> point, Vector3F normal, Color color, Vector2F texture)
    {
        if (!vertexOf.TryGetValue(key, out var vertex))
        {
            vertex = Points.Count;
            vertexOf.Add(key, vertex);
            Points.Add(point);
            Normals.Add(normal);
            Colors.Add(color);
            TextureCoordinates.Add(texture);
        }

        Indices.Add(vertex);
    }
}

/// <summary>What makes a vertex of a PRC mesh: the indices of its point, normal and texture coordinates, and for a
/// vertex coloured on its own, which corner it is.</summary>
internal readonly struct PrcVertexKey : IEquatable<PrcVertexKey>
{
    internal PrcVertexKey(long point, long normal, long texture, long corner)
    {
        Point = point;
        Normal = normal;
        Texture = texture;
        Corner = corner;
    }

    internal long Point { get; }

    internal long Normal { get; }

    internal long Texture { get; }

    internal long Corner { get; }

    public bool Equals(PrcVertexKey other) =>
        Point == other.Point && Normal == other.Normal && Texture == other.Texture && Corner == other.Corner;

    public override bool Equals(object other) => other is PrcVertexKey key && Equals(key);

    public override int GetHashCode() => HashCode.Combine(Point, Normal, Texture, Corner);
}

/// <summary>Turns PRC tessellations into meshes: the triangles, fans and stripes of a 3D tessellation's faces (PRC
/// Tessellation Types), the polylines of a wire tessellation, and the polylines, triangles, quads, polygons and points
/// of a markup tessellation. Every index is checked against its array; one past it throws as corrupt data.</summary>
internal static class PrcMeshBuilder
{
    private const uint IsClosing = 0x10000000;
    private const uint IsContinuous = 0x20000000;
    private const uint WireCountMask = 0x0FFFFFFF;
    private const uint MarkupIsMatrix = 0x08000000;
    private const uint MarkupIsExtraData = 0x04000000;
    private const uint MarkupInnerCodes = 0xFFFFF;
    private const uint MarkupExtraDataType = 0x3E00000;
    private const int MarkupExtraDataShift = 21;
    private const uint MarkupPattern = 0;
    private const uint MarkupTriangles = 2;
    private const uint MarkupQuads = 3;
    private const uint MarkupFaceView = 6;
    private const uint MarkupFrameDraw = 7;
    private const uint MarkupFixedSize = 8;
    private const uint MarkupColor = 11;
    private const uint MarkupText = 14;
    private const uint MarkupPoints = 15;
    private const uint MarkupPolygon = 16;

    /// <summary>The meshes of a 3D tessellation, one per material: <paramref name="materialOf"/> gives each face's.</summary>
    internal static Dictionary<string, PrcMeshData> Build3D(Prc3DTessellation tessellation, Func<PrcFaceTessellation, string> materialOf,
        PrcNotes notes)
    {
        var meshes = new Dictionary<string, PrcMeshData>(StringComparer.Ordinal);
        long corner = 0;
        long next = 0;
        foreach (var face in tessellation.Faces)
        {
            var material = materialOf(face) ?? string.Empty;
            if (!meshes.TryGetValue(material, out var mesh))
            {
                mesh = new PrcMeshData(PrimitiveType.TriangleList)
                {
                    HasNormals = !tessellation.MustRecalculateNormals,
                    HasTextureCoordinates = tessellation.TextureCoordinates.Length > 0
                };
                meshes.Add(material, mesh);
            }

            var cursor = new PrcFaceCursor(tessellation, face, mesh, notes, tessellation.MustRecalculateNormals ? next : face.StartTriangulated);
            cursor.Read(ref corner);
            next = cursor.Position;
        }

        return meshes;
    }

    /// <summary>The line mesh of a wire tessellation: each wire a polyline, closed or continuing the previous one when
    /// its flags say so; with no wire indices, one polyline through all the points.</summary>
    internal static PrcMeshData BuildWire(PrcWireTessellation wire)
    {
        var mesh = new PrcMeshData(PrimitiveType.LineList) { HasNormals = false };
        var pointCount = wire.Coordinates.Length / 3;
        if (wire.WireIndices.Length == 0)
        {
            for (var point = 0; point + 1 < pointCount; point++)
            {
                AddPointCorner(mesh, wire.Coordinates, point * 3L);
                AddPointCorner(mesh, wire.Coordinates, (point + 1) * 3L);
            }

            return mesh;
        }

        long previousLast = -1;
        for (var at = 0; at < wire.WireIndices.Length;)
        {
            var header = wire.WireIndices[at++];
            var count = header & WireCountMask;
            if (count > wire.WireIndices.Length - at)
            {
                throw new InvalidDataException($"A PRC wire tessellation names {count} indices where {wire.WireIndices.Length - at} are left");
            }

            long first = -1;
            long last = previousLast;
            for (var index = 0; index < count; index++)
            {
                long point = wire.WireIndices[at + index];
                CheckPoint(wire.Coordinates, point, "a wire tessellation");
                if (index == 0)
                {
                    first = point;
                    if ((header & IsContinuous) != 0 && previousLast >= 0)
                    {
                        AddPointCorner(mesh, wire.Coordinates, previousLast);
                        AddPointCorner(mesh, wire.Coordinates, point);
                    }
                }
                else
                {
                    AddPointCorner(mesh, wire.Coordinates, last);
                    AddPointCorner(mesh, wire.Coordinates, point);
                }

                last = point;
            }

            if ((header & IsClosing) != 0 && count > 1)
            {
                AddPointCorner(mesh, wire.Coordinates, last);
                AddPointCorner(mesh, wire.Coordinates, first);
            }

            previousLast = last;
            at += (int)count;
        }

        return mesh;
    }

    /// <summary>The meshes of a markup tessellation - its lines and its filled triangles - drawn in the colours its
    /// colour entities give; <paramref name="colorOf"/> turns a colour index of the globals into a colour. Text and
    /// what is drawn in screen space are told and left out.</summary>
    internal static PrcMarkupMeshes BuildMarkup(PrcMarkupTessellation markup, Func<uint, Color> colorOf, Color startColor, PrcNotes notes)
    {
        var meshes = new PrcMarkupMeshes();
        var coordinates = markup.Coordinates;
        var codes = markup.Codes;
        var matrices = new Stack<Matrix4x4<double>>();
        var modes = new Stack<uint>();
        var transform = Matrix4x4<double>.Identity;
        var color = startColor;
        var screenSpace = 0;
        long next = 0;
        var position = 0;
        while (position < codes.Length)
        {
            if (position + 1 >= codes.Length)
            {
                throw new InvalidDataException("A PRC markup tessellation ends in the middle of an entity's codes");
            }

            var code = codes[position];
            var doubles = codes[position + 1];
            var inner = code & MarkupInnerCodes;
            position += 2;
            if (doubles > coordinates.Length - next)
            {
                throw new InvalidDataException($"A PRC markup entity takes {doubles} coordinates where {coordinates.Length - next} are left");
            }

            if ((code & MarkupIsMatrix) != 0)
            {
                if (doubles == 0 && inner == 0)
                {
                    if (matrices.Count > 0)
                    {
                        transform = matrices.Pop();
                    }

                    continue;
                }

                if (doubles < 16)
                {
                    throw new InvalidDataException("A PRC markup matrix block has fewer than its 16 coordinates");
                }

                var values = new double[16];
                Array.Copy(coordinates, next, values, 0, 16);
                next += 16;
                matrices.Push(transform);
                transform = new Matrix4x4<double>(values) * transform;
                continue;
            }

            if ((code & MarkupIsExtraData) == 0)
            {
                if (screenSpace == 0)
                {
                    AddPolyline(meshes.Lines, coordinates, next, doubles / 3, transform, color);
                }

                next += doubles;
                position += (int)Math.Min(inner, (uint)(codes.Length - position));
                continue;
            }

            var type = (code & MarkupExtraDataType) >> MarkupExtraDataShift;
            if (type is MarkupFaceView or MarkupFrameDraw or MarkupFixedSize)
            {
                if (doubles == 0)
                {
                    if (modes.Count > 0 && modes.Pop() == MarkupFrameDraw)
                    {
                        screenSpace--;
                    }

                    if (matrices.Count > 0)
                    {
                        transform = matrices.Pop();
                    }

                    continue;
                }

                if (doubles < 3)
                {
                    throw new InvalidDataException("A PRC markup block starts with fewer than its 3 coordinates");
                }

                modes.Push(type);
                matrices.Push(transform);
                if (type == MarkupFrameDraw)
                {
                    screenSpace++;
                    notes.Tell("markups drawn in the screen's 2D space (frame draw mode) are left out");
                }
                else
                {
                    notes.Tell("markups drawn facing the viewer or at a fixed size are drawn in the model's space, as they lie");
                    var origin = Matrix4x4.CreateTranslation<double>(coordinates[next], coordinates[next + 1], coordinates[next + 2]);
                    transform = origin * transform;
                }

                next += 3;
                continue;
            }

            if (inner > codes.Length - position)
            {
                throw new InvalidDataException("A PRC markup entity names more inner codes than are left");
            }

            switch (type)
            {
                case MarkupColor when inner > 0:
                    color = colorOf(codes[position]);
                    break;
                case MarkupTriangles when screenSpace == 0:
                    AddTriangles(meshes.Triangles, coordinates, next, doubles / 9, 3, transform, color);
                    break;
                case MarkupQuads when screenSpace == 0:
                    AddTriangles(meshes.Triangles, coordinates, next, doubles / 12, 4, transform, color);
                    break;
                case MarkupPolygon when screenSpace == 0:
                    AddTriangles(meshes.Triangles, coordinates, next, 1, (int)(doubles / 3), transform, color);
                    break;
                case MarkupPoints when screenSpace == 0:
                    for (var point = 0L; point < doubles / 3; point++)
                    {
                        meshes.Points.AddCorner(new PrcVertexKey(next + point * 3, -1, -1, meshes.Points.Points.Count),
                            Transform(coordinates, next + point * 3, transform), Vector3F.Zero, color, Vector2F.Zero);
                    }

                    break;
                case MarkupText:
                    notes.Tell("the text of markups is left out");
                    break;
                case MarkupPattern:
                    notes.Tell("the fill patterns of markups are left out");
                    break;
                default:
                    notes.Tell($"markup entities of type {type} are left out");
                    break;
            }

            next += doubles;
            position += (int)inner;
        }

        return meshes;
    }

    private static void AddPolyline(PrcMeshData mesh, double[] coordinates, long first, long count, Matrix4x4<double> transform, Color color)
    {
        for (var point = 1L; point < count; point++)
        {
            var from = first + (point - 1) * 3;
            var to = first + point * 3;
            mesh.AddCorner(new PrcVertexKey(from, -1, -1, color.ToRgba()), Transform(coordinates, from, transform), Vector3F.Zero, color, Vector2F.Zero);
            mesh.AddCorner(new PrcVertexKey(to, -1, -1, color.ToRgba()), Transform(coordinates, to, transform), Vector3F.Zero, color, Vector2F.Zero);
        }
    }

    private static void AddTriangles(PrcMeshData mesh, double[] coordinates, long first, long polygons, int corners,
        Matrix4x4<double> transform, Color color)
    {
        for (var polygon = 0L; polygon < polygons; polygon++)
        {
            var start = first + polygon * corners * 3;
            for (var fan = 1; fan + 1 < corners; fan++)
            {
                foreach (var corner in new[] { 0, fan, fan + 1 })
                {
                    var at = start + corner * 3L;
                    mesh.AddCorner(new PrcVertexKey(at, -1, -1, color.ToRgba()), Transform(coordinates, at, transform), Vector3F.Zero, color, Vector2F.Zero);
                }
            }
        }
    }

    private static Vector3<double> Transform(double[] coordinates, long at, Matrix4x4<double> transform)
    {
        var point = new Vector3<double>(coordinates[at], coordinates[at + 1], coordinates[at + 2]);
        return Vector3<double>.TransformCoordinate(point, transform);
    }

    private static void AddPointCorner(PrcMeshData mesh, double[] coordinates, long point)
    {
        CheckPoint(coordinates, point, "a wire tessellation");
        mesh.AddCorner(new PrcVertexKey(point, -1, -1, 0), new Vector3<double>(coordinates[point], coordinates[point + 1], coordinates[point + 2]),
            Vector3F.Zero, default, Vector2F.Zero);
    }

    /// <summary>Throws unless <paramref name="index"/> starts a whole triple of <paramref name="values"/>.</summary>
    internal static void CheckPoint(double[] values, long index, string what)
    {
        if (index < 0 || index + 2 >= values.Length)
        {
            throw new InvalidDataException($"An index of {what} points at coordinate {index} of {values.Length}");
        }
    }
}

/// <summary>The meshes a markup tessellation gives: its lines, its filled triangles and its points.</summary>
internal sealed class PrcMarkupMeshes
{
    internal PrcMeshData Lines { get; } = new(PrimitiveType.LineList) { HasNormals = false, HasColors = true };

    internal PrcMeshData Triangles { get; } = new(PrimitiveType.TriangleList) { HasNormals = false, HasColors = true };

    internal PrcMeshData Points { get; } = new(PrimitiveType.PointList) { HasNormals = false, HasColors = true };
}

/// <summary>Walks the triangulated data of one face of a 3D tessellation: for each kind of triangulation its flags
/// name, lowest bit first, the counts of its sizes and the normal, texture and point indices they cover.</summary>
internal sealed class PrcFaceCursor
{
    private readonly Prc3DTessellation tessellation;
    private readonly PrcFaceTessellation face;
    private readonly PrcMeshData mesh;
    private readonly PrcNotes notes;
    private readonly int textureIndexes;
    private long position;
    private int sizeAt;
    private int colorAt;

    /// <summary>A cursor at <paramref name="start"/> of the triangulated indices: the face's own start, or - when the
    /// tessellation stores no normals, and its faces' starts still count them - where the previous face ended.</summary>
    internal PrcFaceCursor(Prc3DTessellation tessellation, PrcFaceTessellation face, PrcMeshData mesh, PrcNotes notes, long start)
    {
        this.tessellation = tessellation;
        this.face = face;
        this.mesh = mesh;
        this.notes = notes;
        textureIndexes = (int)Math.Min(face.TextureCoordinateIndexes, 64);
        position = start;
        if (face.VertexColors != null)
        {
            mesh.HasColors = true;
        }
    }

    /// <summary>The triangulated index after the last one read.</summary>
    internal long Position => position;

    internal void Read(ref long corner)
    {
        for (var bit = 0; bit < 32; bit++)
        {
            var kind = 1u << bit;
            if ((face.UsedEntities & kind) == 0)
            {
                continue;
            }

            var textured = kind is PrcTessellationReader.TriangleTextured or PrcTessellationReader.TriangleFanTextured
                or PrcTessellationReader.TriangleStripeTextured or PrcTessellationReader.TriangleOneNormalTextured
                or PrcTessellationReader.TriangleFanOneNormalTextured or PrcTessellationReader.TriangleStripeOneNormalTextured;
            var textures = textured ? textureIndexes : 0;
            switch (kind)
            {
                case PrcTessellationReader.Triangle or PrcTessellationReader.TriangleTextured:
                    var triangles = NextSize();
                    for (var triangle = 0u; triangle < triangles; triangle++)
                    {
                        AddRun(3, false, false, textures, ref corner);
                    }

                    break;
                case PrcTessellationReader.TriangleOneNormal or PrcTessellationReader.TriangleOneNormalTextured:
                    var flat = NextSize();
                    for (var triangle = 0u; triangle < flat; triangle++)
                    {
                        AddRun(3, true, false, textures, ref corner);
                    }

                    break;
                case PrcTessellationReader.TriangleFan or PrcTessellationReader.TriangleFanTextured
                    or PrcTessellationReader.TriangleStripe or PrcTessellationReader.TriangleStripeTextured:
                    var runs = NextSize();
                    for (var run = 0u; run < runs; run++)
                    {
                        AddRun(NextSize(), false, kind is PrcTessellationReader.TriangleStripe or PrcTessellationReader.TriangleStripeTextured,
                            textures, ref corner);
                    }

                    break;
                case PrcTessellationReader.TriangleFanOneNormal or PrcTessellationReader.TriangleFanOneNormalTextured
                    or PrcTessellationReader.TriangleStripeOneNormal or PrcTessellationReader.TriangleStripeOneNormalTextured:
                    var oneNormalRuns = NextSize();
                    for (var run = 0u; run < oneNormalRuns; run++)
                    {
                        var size = NextSize();
                        AddRun(size & ~PrcTessellationReader.SingleNormal, (size & PrcTessellationReader.SingleNormal) != 0,
                            kind is PrcTessellationReader.TriangleStripeOneNormal or PrcTessellationReader.TriangleStripeOneNormalTextured,
                            textures, ref corner);
                    }

                    break;
                default:
                    notes.Tell($"face tessellations of kind 0x{kind:X}, which the specification marks unused, are left out");
                    return;
            }
        }
    }

    private uint NextSize()
    {
        if (sizeAt >= face.SizesTriangulated.Length)
        {
            throw new InvalidDataException("A PRC face tessellation has fewer counts than its kinds of triangulation need");
        }

        return face.SizesTriangulated[sizeAt++];
    }

    private void AddRun(uint count, bool singleNormal, bool stripe, int textures, ref long corner)
    {
        var indices = tessellation.TriangulatedIndices;
        var perVertex = (singleNormal || tessellation.MustRecalculateNormals ? 0 : 1) + textures + 1;
        var needed = (decimal)count * perVertex + (singleNormal && !tessellation.MustRecalculateNormals ? 1 : 0);
        if (needed > indices.Length - position)
        {
            throw new InvalidDataException($"A PRC face tessellation needs {needed} triangulated indices where {indices.Length - position} are left");
        }

        long sharedNormal = -1;
        if (singleNormal && !tessellation.MustRecalculateNormals)
        {
            sharedNormal = indices[position++];
        }

        var vertices = new PrcRunVertex[count];
        for (var vertex = 0; vertex < count; vertex++)
        {
            var normal = sharedNormal;
            if (!singleNormal && !tessellation.MustRecalculateNormals)
            {
                normal = indices[position++];
            }

            long texture = -1;
            for (var layer = 0; layer < textures; layer++)
            {
                var index = indices[position++];
                if (layer == 0)
                {
                    texture = index;
                }
            }

            vertices[vertex] = new PrcRunVertex(indices[position++], normal, texture, NextColor());
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

    private Color NextColor()
    {
        if (face.VertexColors == null)
        {
            return default;
        }

        return colorAt < face.VertexColors.Count ? face.VertexColors[colorAt++] : face.VertexColors[^1];
    }

    private void AddTriangle(PrcRunVertex first, PrcRunVertex second, PrcRunVertex third, ref long corner)
    {
        foreach (var vertex in new[] { first, second, third })
        {
            PrcMeshBuilder.CheckPoint(tessellation.Coordinates, vertex.Point, "a 3D tessellation");
            var point = new Vector3<double>(tessellation.Coordinates[vertex.Point], tessellation.Coordinates[vertex.Point + 1],
                tessellation.Coordinates[vertex.Point + 2]);
            var normal = Vector3F.Zero;
            if (mesh.HasNormals)
            {
                PrcMeshBuilder.CheckPoint(tessellation.Normals, vertex.Normal, "a 3D tessellation's normals");
                normal = new Vector3F((float)tessellation.Normals[vertex.Normal], (float)tessellation.Normals[vertex.Normal + 1],
                    (float)tessellation.Normals[vertex.Normal + 2]);
            }

            var texture = Vector2F.Zero;
            if (vertex.Texture >= 0)
            {
                if (vertex.Texture + 1 >= tessellation.TextureCoordinates.Length)
                {
                    throw new InvalidDataException($"A PRC texture index points at coordinate {vertex.Texture} of {tessellation.TextureCoordinates.Length}");
                }

                texture = new Vector2F((float)tessellation.TextureCoordinates[vertex.Texture], (float)tessellation.TextureCoordinates[vertex.Texture + 1]);
            }

            var key = new PrcVertexKey(vertex.Point, vertex.Normal, vertex.Texture, face.VertexColors != null ? corner : -1);
            corner++;
            mesh.AddCorner(key, point, normal, vertex.Color, texture);
        }
    }
}

/// <summary>A vertex of a run of triangulated data: its point, normal and texture indices and its colour.</summary>
internal readonly struct PrcRunVertex
{
    internal PrcRunVertex(long point, long normal, long texture, Color color)
    {
        Point = point;
        Normal = normal;
        Texture = texture;
        Color = color;
    }

    internal long Point { get; }

    internal long Normal { get; }

    internal long Texture { get; }

    internal Color Color { get; }
}
