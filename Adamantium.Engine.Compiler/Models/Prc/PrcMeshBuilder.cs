using System;
using System.Collections.Generic;
using System.IO;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Turns PRC tessellations into meshes: the triangles, fans and stripes of a 3D tessellation's faces (PRC
/// Tessellation Types), the polylines of a wire tessellation, and the polylines, triangles, quads, polygons and points
/// of a markup tessellation. Every index is checked against its array; one past it throws as corrupt data.</summary>
internal static class PrcMeshBuilder
{
    /// <summary>The flag of a wire index header whose wire closes on its first point.</summary>
    internal const uint IsClosing = 0x10000000;

    /// <summary>The flag of a wire index header whose wire goes on from the previous one.</summary>
    internal const uint IsContinuous = 0x20000000;

    /// <summary>The bits of a wire index header that count its indices.</summary>
    internal const uint WireCountMask = 0x0FFFFFFF;

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
    private const uint MarkupFont = 13;
    private const uint MarkupText = 14;
    private const uint MarkupPoints = 15;
    private const uint MarkupPolygon = 16;

    /// <summary>The meshes of a 3D tessellation, one per material: <paramref name="materialOf"/> gives each face's.</summary>
    internal static Dictionary<string, PrcMeshData> Build3D(Prc3DTessellation tessellation, Func<PrcFaceTessellation, string> materialOf,
        ImportNotes notes)
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

        if (tessellation.MustRecalculateNormals)
        {
            PrcCreaseNormals.Apply(meshes.Values, tessellation.CreaseAngle);
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

    /// <summary>The line, triangle and point meshes of a markup tessellation and its texts; <paramref name="colorOf"/>
    /// turns a color index of the globals into a color.</summary>
    internal static PrcMarkupMeshes BuildMarkup(PrcMarkupTessellation markup, Func<uint, Color> colorOf, Color startColor, ImportNotes notes)
    {
        var meshes = new PrcMarkupMeshes();
        var coordinates = markup.Coordinates;
        var codes = markup.Codes;
        var matrices = new Stack<Matrix4x4<double>>();
        var modes = new Stack<uint>();
        var transform = Matrix4x4<double>.Identity;
        var color = startColor;
        var font = -1;
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
                            TransformPoint(coordinates, next + point * 3, transform), Vector3F.Zero, color, Vector2F.Zero);
                    }

                    break;
                case MarkupFont when inner > 0:
                    font = (int)codes[position];
                    break;
                case MarkupText when screenSpace == 0 && inner > 0:
                    var index = (int)Math.Min(codes[position], int.MaxValue);
                    meshes.Texts.Add(new PrcMarkupText
                    {
                        Text = index < markup.Texts.Count ? markup.Texts[index] : null,
                        FontIndex = font,
                        Width = doubles > 0 ? coordinates[next] : 0,
                        Height = doubles > 1 ? coordinates[next + 1] : 0,
                        Position = Vector3<double>.TransformCoordinate(Vector3<double>.Zero, transform)
                    });
                    if (index >= markup.Texts.Count)
                    {
                        notes.Tell("a markup text entity names a string its tessellation does not hold");
                    }

                    break;
                case MarkupFont or MarkupText:
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

    /// <summary>Throws unless <paramref name="index"/> starts a whole triple of <paramref name="values"/>.</summary>
    internal static void CheckPoint(double[] values, long index, string what)
    {
        if (index < 0 || index + 2 >= values.Length)
        {
            throw new InvalidDataException($"An index of {what} points at coordinate {index} of {values.Length}");
        }
    }

    private static void AddPolyline(PrcMeshData mesh, double[] coordinates, long first, long count, Matrix4x4<double> transform, Color color)
    {
        for (var point = 1L; point < count; point++)
        {
            var from = first + (point - 1) * 3;
            var to = first + point * 3;
            mesh.AddCorner(new PrcVertexKey(from, -1, -1, color.ToRgba()), TransformPoint(coordinates, from, transform), Vector3F.Zero, color, Vector2F.Zero);
            mesh.AddCorner(new PrcVertexKey(to, -1, -1, color.ToRgba()), TransformPoint(coordinates, to, transform), Vector3F.Zero, color, Vector2F.Zero);
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
                int[] triangleCorners = [0, fan, fan + 1];
                foreach (var corner in triangleCorners)
                {
                    var at = start + corner * 3L;
                    mesh.AddCorner(new PrcVertexKey(at, -1, -1, color.ToRgba()), TransformPoint(coordinates, at, transform), Vector3F.Zero, color, Vector2F.Zero);
                }
            }
        }
    }

    private static Vector3<double> TransformPoint(double[] coordinates, long at, Matrix4x4<double> transform)
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
}
