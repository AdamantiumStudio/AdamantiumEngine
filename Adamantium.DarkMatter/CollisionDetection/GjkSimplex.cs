using System;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>The one to four points of the difference of two cores that GJK keeps, cut each time to the fewest that hold
/// the point nearest the origin, with that point's weights on them.</summary>
internal sealed class GjkSimplex
{
    // Each face's three corners, then the corner across from it. A static array: a span made of a constant list is
    // reflection on every call where the JIT does not make it plain data - in Debug, say.
    private static readonly int[] FaceCorners = [0, 1, 2, 3, 0, 2, 3, 1, 0, 3, 1, 2, 1, 3, 2, 0];

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
                nearest = NearestOnTriangle();
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
    private Vector3<double> NearestOnTriangle()
    {
        var firstPoint = Vertices[0].Point;
        var secondPoint = Vertices[1].Point;
        var thirdPoint = Vertices[2].Point;
        TriangleMath.FindNearestWeights(firstPoint, secondPoint, thirdPoint, Vector3<double>.Zero, out var firstWeight,
            out var secondWeight, out var thirdWeight);
        var nearest = firstPoint * firstWeight + secondPoint * secondWeight + thirdPoint * thirdWeight;
        KeepWeighed(0, 1, 2, firstWeight, secondWeight, thirdWeight);
        return nearest;
    }

    // The faces the origin may lie beyond are weighed by their points alone; the simplex is cut once, to the best.
    private bool TryNearestOnTetrahedron(out Vector3<double> nearest)
    {
        ReadOnlySpan<int> faces = FaceCorners;
        var nearestFace = new NearestFace { Face = -1, Distance = double.MaxValue };
        // The triangle the newest corner was found from had the nearest point inside it, and the origin is on that
        // corner's side of it: it is weighed when flat there, and by its side only when - rounding having had its way -
        // no other face is.
        for (var face = 1; face < 4; face++)
        {
            WeighFace(faces, face, true, ref nearestFace);
        }

        WeighFace(faces, 0, nearestFace.Face < 0, ref nearestFace);
        nearest = nearestFace.Nearest;
        if (nearestFace.Face < 0)
            return false;

        var corners = faces.Slice(nearestFace.Face * 4, 3);
        KeepWeighed(corners[0], corners[1], corners[2], nearestFace.FirstWeight, nearestFace.SecondWeight,
            nearestFace.ThirdWeight);
        return true;
    }

    // The face's nearest point, kept when nearer than the nearest yet: a flat face, or - when told by its side - one the
    // origin is beyond.
    private void WeighFace(ReadOnlySpan<int> faces, int face, bool byItsSide, ref NearestFace nearestFace)
    {
        var first = Vertices[faces[face * 4]].Point;
        var second = Vertices[faces[face * 4 + 1]].Point;
        var third = Vertices[faces[face * 4 + 2]].Point;
        var across = Vertices[faces[face * 4 + 3]].Point;
        var normal = Vector3<double>.Cross(second - first, third - first);
        var originSide = -Vector3<double>.Dot(first, normal);
        var acrossSide = Vector3<double>.Dot(across - first, normal);
        var isFlat = acrossSide * acrossSide <= 1e-24 * normal.LengthSquared();
        if (!isFlat && !(byItsSide && originSide * acrossSide <= 0))
            return;

        TriangleMath.FindNearestWeights(first, second, third, Vector3<double>.Zero, out var firstWeight,
            out var secondWeight, out var thirdWeight);
        var candidate = first * firstWeight + second * secondWeight + third * thirdWeight;
        var distance = candidate.LengthSquared();
        if (distance >= nearestFace.Distance)
            return;

        nearestFace = new NearestFace
        {
            Face = face, Distance = distance, Nearest = candidate, FirstWeight = firstWeight,
            SecondWeight = secondWeight, ThirdWeight = thirdWeight
        };
    }

    // The corners of the three given that weigh in the nearest point, as a triangle cuts them.
    private void KeepWeighed(int first, int second, int third, double firstWeight, double secondWeight,
        double thirdWeight)
    {
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
