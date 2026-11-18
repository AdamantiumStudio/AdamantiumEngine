using System;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>The point of a triangle nearest a point, as weights of its corners, for GJK and EPA alike.</summary>
internal static class TriangleMath
{
    /// <summary>By the areas the point's foot cuts the triangle into when it falls inside, else the nearest point of
    /// the nearest edge; a corner left out weighs exactly zero.</summary>
    public static void FindNearestWeights(Vector3<double> first, Vector3<double> second, Vector3<double> third,
        Vector3<double> point, out double firstWeight, out double secondWeight, out double thirdWeight)
    {
        // A sliver's normal is lost in rounding, and the weights by it with it: its edges tell the nearest point.
        var normal = Vector3<double>.Cross(second - first, third - first);
        var twiceAreaSquared = normal.LengthSquared();
        if (twiceAreaSquared > 1e-18 * (second - first).LengthSquared() * (third - first).LengthSquared())
        {
            var foot = point - normal * (Vector3<double>.Dot(point - first, normal) / twiceAreaSquared);
            var facingFirst = Vector3<double>.Dot(Vector3<double>.Cross(second - foot, third - foot), normal)
                              / twiceAreaSquared;
            var facingSecond = Vector3<double>.Dot(Vector3<double>.Cross(third - foot, first - foot), normal)
                               / twiceAreaSquared;
            var facingThird = 1 - facingFirst - facingSecond;
            if (facingFirst >= 0 && facingSecond >= 0 && facingThird >= 0)
            {
                firstWeight = facingFirst;
                secondWeight = facingSecond;
                thirdWeight = facingThird;
                return;
            }
        }

        var alongFirstEdge = AlongEdge(first, second, point);
        var alongSecondEdge = AlongEdge(second, third, point);
        var alongThirdEdge = AlongEdge(third, first, point);
        var awayFirstEdge = (first + (second - first) * alongFirstEdge - point).LengthSquared();
        var awaySecondEdge = (second + (third - second) * alongSecondEdge - point).LengthSquared();
        var awayThirdEdge = (third + (first - third) * alongThirdEdge - point).LengthSquared();
        if (awayFirstEdge <= awaySecondEdge && awayFirstEdge <= awayThirdEdge)
        {
            firstWeight = 1 - alongFirstEdge;
            secondWeight = alongFirstEdge;
            thirdWeight = 0;
        }
        else if (awaySecondEdge <= awayThirdEdge)
        {
            firstWeight = 0;
            secondWeight = 1 - alongSecondEdge;
            thirdWeight = alongSecondEdge;
        }
        else
        {
            firstWeight = alongThirdEdge;
            secondWeight = 0;
            thirdWeight = 1 - alongThirdEdge;
        }
    }

    // How far along the edge from start to end its point nearest point lies, from zero at start to one at end.
    private static double AlongEdge(Vector3<double> start, Vector3<double> end, Vector3<double> point)
    {
        var edge = end - start;
        var lengthSquared = edge.LengthSquared();
        return lengthSquared > 0 ? Math.Clamp(Vector3<double>.Dot(point - start, edge) / lengthSquared, 0, 1) : 0;
    }
}
