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

    // The triangle's point nearest the origin, the simplex cut to the corners that weigh in it.
    private Vector3<double> NearestOnTriangle(int first, int second, int third)
    {
        var firstPoint = Vertices[first].Point;
        var secondPoint = Vertices[second].Point;
        var thirdPoint = Vertices[third].Point;
        TriangleMath.FindNearestWeights(firstPoint, secondPoint, thirdPoint, Vector3<double>.Zero, out var firstWeight,
            out var secondWeight, out var thirdWeight);
        var nearest = firstPoint * firstWeight + secondPoint * secondWeight + thirdPoint * thirdWeight;
        if (firstWeight > 0 && secondWeight > 0 && thirdWeight > 0)
            Keep(first, second, third, firstWeight, secondWeight, thirdWeight);
        else if (thirdWeight == 0 && firstWeight > 0 && secondWeight > 0)
            Keep(first, second, firstWeight, secondWeight);
        else if (firstWeight == 0 && secondWeight > 0 && thirdWeight > 0)
            Keep(second, third, secondWeight, thirdWeight);
        else if (secondWeight == 0 && firstWeight > 0 && thirdWeight > 0)
            Keep(first, third, firstWeight, thirdWeight);
        else
            Keep(firstWeight > 0 ? first : secondWeight > 0 ? second : third);

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
