using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Compiler.Models.Prc;

/// <summary>Works out recalculated normals: triangles meeting within the crease angle share a normal, past it the edge
/// stays sharp.</summary>
internal static class PrcCreaseNormals
{
    private const double StraightAngle = 180;

    /// <summary>Sets the corner normals of the triangle meshes by <paramref name="creaseAngle"/>, in degrees; below zero
    /// every triangle stays flat.</summary>
    internal static void Apply(IEnumerable<PrcMeshData> meshes, double creaseAngle)
    {
        var triangleMeshes = meshes.Where(mesh => mesh.Topology == PrimitiveType.TriangleList).ToList();
        var limit = creaseAngle < 0 ? double.PositiveInfinity : Math.Cos(Math.Min(creaseAngle, StraightAngle) * Math.PI / StraightAngle);
        List<PrcCreaseCorner> corners = [];
        var around = new Dictionary<Vector3<double>, List<int>>();
        foreach (var mesh in triangleMeshes)
        {
            for (var start = 0; start + 2 < mesh.Indices.Count; start += 3)
            {
                var first = mesh.Points[mesh.Indices[start]];
                var second = mesh.Points[mesh.Indices[start + 1]];
                var third = mesh.Points[mesh.Indices[start + 2]];
                var normal = Vector3<double>.Normalize(Vector3<double>.Cross(second - first, third - first));
                if (!normal.IsNormalized)
                {
                    normal = Vector3<double>.Zero;
                }

                AddCorner(corners, around, first, normal, GetAngleAt(first, second, third));
                AddCorner(corners, around, second, normal, GetAngleAt(second, third, first));
                AddCorner(corners, around, third, normal, GetAngleAt(third, first, second));
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

                var smooth = Vector3<double>.Normalize(sum);
                var chosen = smooth.IsNormalized ? smooth : own;
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

    private static double GetAngleAt(Vector3<double> point, Vector3<double> next, Vector3<double> previous)
    {
        var toNext = Vector3<double>.Normalize(next - point);
        var toPrevious = Vector3<double>.Normalize(previous - point);
        if (!toNext.IsNormalized || !toPrevious.IsNormalized)
        {
            return 0;
        }

        return Math.Acos(Math.Clamp(Vector3<double>.Dot(toNext, toPrevious), -1, 1));
    }
}
