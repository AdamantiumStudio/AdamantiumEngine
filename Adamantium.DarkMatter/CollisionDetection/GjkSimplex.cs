using System;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>The one to four points of the difference of two cores that GJK keeps, cut each time to the fewest that hold
/// the point nearest the origin, with that point's weights on them.</summary>
internal sealed class GjkSimplex
{
    public readonly SupportVertex[] Vertices = new SupportVertex[4];
    public readonly double[] Weights = new double[4];
    public int Count;

    public void Clear() => Count = 0;

    public void Add(in SupportVertex vertex) => Vertices[Count++] = vertex;

    /// <summary>The point of the simplex nearest the origin, the simplex cut to the points that hold it and their weights
    /// set; false when the origin is inside a tetrahedron.</summary>
    public bool TryFindNearest(out Vector3<double> nearest)
    {
        switch (Count)
        {
            case 1:
                Weights[0] = 1;
                nearest = Vertices[0].Point;
                return true;
            case 2:
                nearest = NearestOnSegment(0, 1);
                return true;
            case 3:
                nearest = NearestOnTriangle(0, 1, 2);
                return true;
            default:
                return TryNearestOnTetrahedron(out nearest);
        }
    }

    /// <summary>The weighted points of each core the nearest point is made of.</summary>
    public void FindWitnesses(out Vector3F onFirst, out Vector3F onSecond)
    {
        var first = Vector3<double>.Zero;
        var second = Vector3<double>.Zero;
        for (var index = 0; index < Count; index++)
        {
            first += Vertices[index].OnFirst.As<double>() * Weights[index];
            second += Vertices[index].OnSecond.As<double>() * Weights[index];
        }

        onFirst = first.As<float>();
        onSecond = second.As<float>();
    }

    private Vector3<double> NearestOnSegment(int first, int second)
    {
        var a = Vertices[first].Point;
        var b = Vertices[second].Point;
        var along = b - a;
        var lengthSquared = along.LengthSquared();
        var t = lengthSquared > 0 ? -Vector3<double>.Dot(a, along) / lengthSquared : 0;
        if (t <= 0)
            return Keep(first);

        if (t >= 1)
            return Keep(second);

        Keep(first, second, 1 - t, t);
        return a + along * t;
    }

    // Ericson, Real-Time Collision Detection, 5.1.5: the region of the triangle the origin projects into.
    private Vector3<double> NearestOnTriangle(int first, int second, int third)
    {
        var a = Vertices[first].Point;
        var b = Vertices[second].Point;
        var c = Vertices[third].Point;
        var ab = b - a;
        var ac = c - a;
        var d1 = -Vector3<double>.Dot(ab, a);
        var d2 = -Vector3<double>.Dot(ac, a);
        if (d1 <= 0 && d2 <= 0)
            return Keep(first);

        var d3 = -Vector3<double>.Dot(ab, b);
        var d4 = -Vector3<double>.Dot(ac, b);
        if (d3 >= 0 && d4 <= d3)
            return Keep(second);

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            var v = d1 / (d1 - d3);
            Keep(first, second, 1 - v, v);
            return a + ab * v;
        }

        var d5 = -Vector3<double>.Dot(ab, c);
        var d6 = -Vector3<double>.Dot(ac, c);
        if (d6 >= 0 && d5 <= d6)
            return Keep(third);

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            var w = d2 / (d2 - d6);
            Keep(first, third, 1 - w, w);
            return a + ac * w;
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            var w = (d4 - d3) / (d4 - d3 + (d5 - d6));
            Keep(second, third, 1 - w, w);
            return b + (c - b) * w;
        }

        var sum = va + vb + vc;
        if (!(sum > 0))
            return NearestOnFlatTriangle(first, second, third);

        var weightB = vb / sum;
        var weightC = vc / sum;
        Keep(first, second, third, 1 - weightB - weightC, weightB, weightC);
        return a + ab * weightB + ac * weightC;
    }

    // A triangle of no area: the nearest point of its three edges.
    private Vector3<double> NearestOnFlatTriangle(int first, int second, int third)
    {
        Span<SupportVertex> corners = [Vertices[first], Vertices[second], Vertices[third]];
        Span<SupportVertex> kept = stackalloc SupportVertex[2];
        Span<double> keptWeights = stackalloc double[2];
        var keptCount = 0;
        var nearest = Vector3<double>.Zero;
        var best = double.MaxValue;
        for (var edge = 0; edge < 3; edge++)
        {
            Vertices[0] = corners[edge];
            Vertices[1] = corners[(edge + 1) % 3];
            Count = 2;
            var candidate = NearestOnSegment(0, 1);
            var distance = candidate.LengthSquared();
            if (distance >= best)
                continue;

            best = distance;
            nearest = candidate;
            keptCount = Count;
            for (var index = 0; index < Count; index++)
            {
                kept[index] = Vertices[index];
                keptWeights[index] = Weights[index];
            }
        }

        Count = keptCount;
        for (var index = 0; index < keptCount; index++)
        {
            Vertices[index] = kept[index];
            Weights[index] = keptWeights[index];
        }

        return nearest;
    }

    private bool TryNearestOnTetrahedron(out Vector3<double> nearest)
    {
        // Each face's three corners, then the corner across from it.
        ReadOnlySpan<int> faces = [0, 1, 2, 3, 0, 2, 3, 1, 0, 3, 1, 2, 1, 3, 2, 0];
        var found = false;
        var best = double.MaxValue;
        nearest = Vector3<double>.Zero;
        Span<SupportVertex> kept = stackalloc SupportVertex[3];
        Span<double> keptWeights = stackalloc double[3];
        var keptCount = 0;
        Span<SupportVertex> all = stackalloc SupportVertex[4];
        Vertices.AsSpan(0, 4).CopyTo(all);
        for (var face = 0; face < 4; face++)
        {
            var a = all[faces[face * 4]].Point;
            var b = all[faces[face * 4 + 1]].Point;
            var c = all[faces[face * 4 + 2]].Point;
            var d = all[faces[face * 4 + 3]].Point;
            var normal = Vector3<double>.Cross(b - a, c - a);
            var originSide = -Vector3<double>.Dot(a, normal);
            var oppositeSide = Vector3<double>.Dot(d - a, normal);
            // Outside the face, or the tetrahedron is flat there: the nearest point may be on it.
            if (originSide * oppositeSide > 0 && oppositeSide * oppositeSide > 1e-24 * normal.LengthSquared())
                continue;

            Vertices[0] = all[faces[face * 4]];
            Vertices[1] = all[faces[face * 4 + 1]];
            Vertices[2] = all[faces[face * 4 + 2]];
            Count = 3;
            var candidate = NearestOnTriangle(0, 1, 2);
            var distance = candidate.LengthSquared();
            if (found && distance >= best)
                continue;

            found = true;
            best = distance;
            nearest = candidate;
            keptCount = Count;
            for (var index = 0; index < Count; index++)
            {
                kept[index] = Vertices[index];
                keptWeights[index] = Weights[index];
            }
        }

        if (!found)
        {
            all.CopyTo(Vertices);
            Count = 4;
            return false;
        }

        Count = keptCount;
        for (var index = 0; index < keptCount; index++)
        {
            Vertices[index] = kept[index];
            Weights[index] = keptWeights[index];
        }

        return true;
    }

    private Vector3<double> Keep(int only)
    {
        Vertices[0] = Vertices[only];
        Weights[0] = 1;
        Count = 1;
        return Vertices[0].Point;
    }

    private void Keep(int first, int second, double firstWeight, double secondWeight)
    {
        var a = Vertices[first];
        var b = Vertices[second];
        Vertices[0] = a;
        Vertices[1] = b;
        Weights[0] = firstWeight;
        Weights[1] = secondWeight;
        Count = 2;
    }

    private void Keep(int first, int second, int third, double firstWeight, double secondWeight, double thirdWeight)
    {
        var a = Vertices[first];
        var b = Vertices[second];
        var c = Vertices[third];
        Vertices[0] = a;
        Vertices[1] = b;
        Vertices[2] = c;
        Weights[0] = firstWeight;
        Weights[1] = secondWeight;
        Weights[2] = thirdWeight;
        Count = 3;
    }
}
