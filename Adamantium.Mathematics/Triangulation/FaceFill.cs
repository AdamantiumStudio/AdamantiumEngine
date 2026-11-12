using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Adamantium.Mathematics.Triangulation;

/// <summary>
/// Fills rings that are simple and cross nowhere, the filled side of each on its left - as
/// <see cref="GeometryOverlay"/> gives them: a ring turning anticlockwise bounds a face, one turning clockwise is a hole
/// in the face straight around it (<see cref="NestingSweep"/>). Each face is cut by clipping ears exactly as its rings
/// lie, measured from a corner of its own so it keeps its digits far from the origin; a face with many holes, or one the
/// clipping does not cover whole, is cut instead into the trapezoids between its edges, each running across x for as
/// long as the two edges bounding it do (<see cref="TrapezoidSweep"/>).
/// </summary>
internal static class FaceFill
{
    private const double AreaTolerance = 1e-6;
    private const int ManyHoles = 64;

    /// <summary>The triangles of the faces <paramref name="rings"/> bound, three points to a triangle, each turning the
    /// way <see cref="MathHelper.Polyline.IsClockwise{T}"/> calls clockwise; none of no area. Rings of fewer than three
    /// points, or with a coordinate not a finite number, are left out. The rings must be as
    /// <see cref="GeometryOverlay"/> gives them.</summary>
    public static List<Vector3<double>> Fill(IReadOnlyList<Vector2<double>[]> rings)
    {
        ArgumentNullException.ThrowIfNull(rings);
        var triangles = new List<Vector3<double>>();
        var kept = new List<Vector2<double>[]>();
        var outer = new List<bool>();
        foreach (var ring in rings)
        {
            if (ring == null || ring.Length < 3 || !MathHelper.RingMath.Finite(ring)) continue;
            var area = MathHelper.RingMath.SignedArea(ring);
            if (area == 0) continue;
            kept.Add(ring);
            outer.Add(area > 0);
        }

        var parent = new NestingSweep(kept).Parents([]);
        var faceHoles = new List<Vector2<double>[]>[kept.Count];
        for (var i = 0; i < kept.Count; i++)
        {
            if (!outer[i] && parent[i] >= 0 && outer[parent[i]]) (faceHoles[parent[i]] ??= []).Add(kept[i]);
        }

        for (var i = 0; i < kept.Count; i++)
        {
            if (outer[i]) FillFace(kept[i], faceHoles[i] ?? [], triangles);
        }

        return triangles;
    }

    /// <summary>Adds to <paramref name="triangles"/> those of the face <paramref name="outer"/> bounds less
    /// <paramref name="holes"/> - simple rings, crossing nowhere, each hole inside the outer ring, either way round.
    /// </summary>
    internal static void FillFace(Vector2<double>[] outer, List<Vector2<double>[]> holes, List<Vector3<double>> triangles)
    {
        var origin = outer[0];
        var face = new List<Vector2<double>[]>(holes.Count + 1) { Moved(outer, origin) };
        foreach (var hole in holes) face.Add(Moved(hole, origin));
        if (holes.Count > ManyHoles || !ClipEars(face, origin, triangles))
        {
            CutIntoTrapezoids(face, origin, triangles);
        }
    }

    private static Vector2<double>[] Moved(Vector2<double>[] ring, Vector2<double> origin)
    {
        var moved = new Vector2<double>[ring.Length];
        for (var i = 0; i < ring.Length; i++) moved[i] = ring[i] - origin;
        return moved;
    }

    private static bool ClipEars(List<Vector2<double>[]> face, Vector2<double> origin, List<Vector3<double>> triangles)
    {
        var points = new List<Vector2<double>>(face[0]);
        var starts = new List<int>(face.Count - 1);
        for (var h = 1; h < face.Count; h++)
        {
            starts.Add(points.Count);
            points.AddRange(face[h]);
        }

        var indices = new List<int>();
        if (!EarClipping.Triangulate(CollectionsMarshal.AsSpan(points), CollectionsMarshal.AsSpan(starts), indices))
        {
            return false;
        }

        var faceArea = Math.Abs(MathHelper.RingMath.SignedArea(face[0]));
        for (var h = 1; h < face.Count; h++) faceArea -= Math.Abs(MathHelper.RingMath.SignedArea(face[h]));
        var cut = 0.0;
        for (var t = 0; t + 2 < indices.Count; t += 3)
        {
            var (a, b, c) = (points[indices[t]], points[indices[t + 1]], points[indices[t + 2]]);
            cut += Math.Abs(Vector2<double>.Determinant(b - a, c - a)) / 2;
        }

        if (Math.Abs(cut - faceArea) > Math.Max(Math.Abs(faceArea), double.Epsilon) * AreaTolerance)
        {
            return false;
        }

        for (var t = 0; t + 2 < indices.Count; t += 3)
        {
            AddTriangle(triangles, points[indices[t]], points[indices[t + 1]], points[indices[t + 2]], origin);
        }

        return true;
    }

    private static void CutIntoTrapezoids(List<Vector2<double>[]> rings, Vector2<double> origin, List<Vector3<double>> triangles)
    {
        new TrapezoidSweep(rings).Run((bottom, top, from, to) =>
        {
            var (a, b) = (new Vector2<double>(from, TrapezoidSweep.At(bottom, from)), new Vector2<double>(to, TrapezoidSweep.At(bottom, to)));
            var (c, d) = (new Vector2<double>(to, TrapezoidSweep.At(top, to)), new Vector2<double>(from, TrapezoidSweep.At(top, from)));
            AddTriangle(triangles, a, b, c, origin);
            AddTriangle(triangles, a, c, d, origin);
        });
    }

    private static void AddTriangle(List<Vector3<double>> triangles, Vector2<double> a, Vector2<double> b, Vector2<double> c,
        Vector2<double> origin)
    {
        var turn = Vector2<double>.Determinant(b - a, c - a);
        if (turn == 0 || double.IsNaN(turn)) return;
        if (turn < 0) (b, c) = (c, b);
        var (pa, pb, pc) = (a + origin, b + origin, c + origin);
        if (Vector2<double>.Determinant(pb - pa, pc - pa) <= 0) return;
        triangles.Add((Vector3<double>)pa);
        triangles.Add((Vector3<double>)pb);
        triangles.Add((Vector3<double>)pc);
    }
}
