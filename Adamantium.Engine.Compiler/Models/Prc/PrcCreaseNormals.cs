using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Works out the normals a 3D tessellation asks to be recalculated by its crease angle (must_recalculate_normals,
/// PRC_TYPE_TESS_3D): where triangles meet at a point, those whose normals are within the crease angle of one another
/// share a normal there, and past it each keeps its own - the edge between them is drawn sharp. A corner's normal is the
/// normals of its own triangle and of every triangle at its point within the crease angle of it, each weighted by the
/// angle it makes at the point. Points are met by their place, so faces and meshes of one tessellation smooth into one
/// another. A crease angle below zero shares no normal: every triangle is drawn flat.</summary>
internal static class PrcCreaseNormals
{
    private const double SmallestLength = 1e-20;
    private const double StraightAngle = 180;

    internal static void Apply(IEnumerable<PrcMeshData> meshes, double creaseAngle)
    {
        var triangleMeshes = meshes.Where(mesh => mesh.Topology == PrimitiveType.TriangleList).ToList();
        var limit = creaseAngle < 0 ? double.PositiveInfinity : Math.Cos(Math.Min(creaseAngle, StraightAngle) * Math.PI / StraightAngle);
        var corners = new List<PrcCreaseCorner>();
        var around = new Dictionary<Vector3<double>, List<int>>();
        foreach (var mesh in triangleMeshes)
        {
            for (var start = 0; start + 2 < mesh.Indices.Count; start += 3)
            {
                var first = mesh.Points[mesh.Indices[start]];
                var second = mesh.Points[mesh.Indices[start + 1]];
                var third = mesh.Points[mesh.Indices[start + 2]];
                var normal = Unit(Vector3<double>.Cross(second - first, third - first));
                AddCorner(corners, around, first, normal, AngleAt(first, second, third));
                AddCorner(corners, around, second, normal, AngleAt(second, third, first));
                AddCorner(corners, around, third, normal, AngleAt(third, first, second));
            }
        }

        var normals = new Vector3F[corners.Count];
        foreach (var shared in around.Values)
        {
            foreach (var corner in shared)
            {
                var own = corners[corner].Normal;
                var sum = Vector3<double>.Zero;
                foreach (var other in shared)
                {
                    var candidate = corners[other];
                    if (other == corner || Vector3<double>.Dot(own, candidate.Normal) >= limit)
                    {
                        sum += candidate.Normal * candidate.Angle;
                    }
                }

                var smooth = Unit(sum);
                var chosen = smooth == Vector3<double>.Zero ? own : smooth;
                normals[corner] = new Vector3F((float)chosen.X, (float)chosen.Y, (float)chosen.Z);
            }
        }

        var next = 0;
        foreach (var mesh in triangleMeshes)
        {
            var count = mesh.Indices.Count / 3 * 3;
            var ofMesh = new Vector3F[mesh.Indices.Count];
            Array.Copy(normals, next, ofMesh, 0, count);
            next += count;
            mesh.SetCornerNormals(ofMesh);
        }
    }

    private static void AddCorner(List<PrcCreaseCorner> corners, Dictionary<Vector3<double>, List<int>> around, Vector3<double> point,
        Vector3<double> normal, double angle)
    {
        if (!around.TryGetValue(point, out var shared))
        {
            shared = [];
            around.Add(point, shared);
        }

        shared.Add(corners.Count);
        corners.Add(new PrcCreaseCorner(normal, angle));
    }

    private static double AngleAt(Vector3<double> point, Vector3<double> next, Vector3<double> previous)
    {
        var toNext = Unit(next - point);
        var toPrevious = Unit(previous - point);
        return Math.Acos(Math.Clamp(Vector3<double>.Dot(toNext, toPrevious), -1, 1));
    }

    private static Vector3<double> Unit(Vector3<double> vector)
    {
        var length = Math.Sqrt(Vector3<double>.Dot(vector, vector));
        return length < SmallestLength ? Vector3<double>.Zero : vector / length;
    }
}

/// <summary>A corner of a triangle as the crease normals see it: its triangle's unit normal - zero for a triangle of no
/// area - and the angle the triangle makes at it, in radians.</summary>
internal readonly struct PrcCreaseCorner
{
    internal PrcCreaseCorner(Vector3<double> normal, double angle)
    {
        Normal = normal;
        Angle = angle;
    }

    internal Vector3<double> Normal { get; }

    internal double Angle { get; }
}
