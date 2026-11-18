using System;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>The nearest points of two cores by GJK (Gilbert, Johnson and Keerthi): the point of their difference
/// nearest the origin, found on a simplex walked toward it, in double so a millimeter beside a huge face is found.
/// </summary>
internal static class GjkClosestPoints
{
    private const int MaxIterations = 64;
    private const double RelativeTolerance = 1e-10;

    /// <summary>True when the cores stand apart, with their distance, the direction from the first to the second and
    /// their nearest points; false when they overlap or touch, <paramref name="simplex"/> left holding what EPA starts
    /// from, the direction then the way the walk last went, zero if it went nowhere. Cores farther apart than
    /// <paramref name="maxDistance"/> are told apart early, the distance then only known to be beyond it.</summary>
    public static bool TryFind(in PlacedCore first, in PlacedCore second, float maxDistance, GjkSimplex simplex,
        out float distance, out Vector3F towardSecond, out Vector3F onFirst, out Vector3F onSecond)
    {
        var found = TryFindNearest(first, second, maxDistance, simplex, out var nearest, out var walked, out onFirst,
            out onSecond);
        distance = found ? (float)nearest.Length() : 0;
        var direction = found ? nearest : walked;
        var length = direction.Length();
        towardSecond = length > 0 ? (-direction / length).As<float>() : Vector3F.Zero;
        return found;
    }

    // The nearest point found and the last one well clear of the origin, whose direction still means something when
    // the walk ends on cores that touch.
    private static bool TryFindNearest(in PlacedCore first, in PlacedCore second, float maxDistance, GjkSimplex simplex,
        out Vector3<double> nearestPoint, out Vector3<double> walked, out Vector3F onFirst, out Vector3F onSecond)
    {
        simplex.Clear();
        var start = (second.Center - first.Center).As<double>();
        if (start.LengthSquared() < 1e-12)
            start = Vector3<double>.UnitX;

        // The difference lies about the first's center less the second's: its point the other way, toward the origin.
        simplex.Add(PlacedCore.SupportOfDifference(first, second, start));
        simplex.Weights[0] = 1;
        var nearest = simplex.Vertices[0].Point;
        // Touching is told by the size of the pair, not by how near the first point happened to be.
        var scale = Math.Max(Math.Max(nearest.Length(), start.Length()), 1e-3);
        var touching = 1e-14 * scale * scale;
        var reachSquared = (double)maxDistance * maxDistance;
        var clear = 1e6 * touching;
        nearestPoint = nearest;
        walked = Vector3<double>.Zero;
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var nearestSquared = nearest.LengthSquared();
            if (nearestSquared > clear)
                walked = nearest;

            if (nearestSquared <= touching)
                return Witness(simplex, false, out onFirst, out onSecond);

            var vertex = PlacedCore.SupportOfDifference(first, second, -nearest);
            var along = Vector3<double>.Dot(nearest, vertex.Point);

            // Every point of the difference lies farther than the reach: apart beyond it.
            if (along > 0 && along * along > reachSquared * nearestSquared)
                return Witness(simplex, true, out onFirst, out onSecond);

            // No point nearer to be had: the nearest is found.
            if (nearestSquared - along <= RelativeTolerance * nearestSquared || IsKept(simplex, vertex.Point))
                return Witness(simplex, true, out onFirst, out onSecond);

            simplex.Add(vertex);
            if (!simplex.TryFindNearest(out var next))
                return Witness(simplex, false, out onFirst, out onSecond);

            var progressed = next.LengthSquared() < nearestSquared;
            nearest = next;
            nearestPoint = nearest;
            if (!progressed)
                return Witness(simplex, true, out onFirst, out onSecond);
        }

        return Witness(simplex, true, out onFirst, out onSecond);
    }

    private static bool Witness(GjkSimplex simplex, bool apart, out Vector3F onFirst, out Vector3F onSecond)
    {
        simplex.FindWitnesses(out onFirst, out onSecond);
        return apart;
    }

    private static bool IsKept(GjkSimplex simplex, Vector3<double> point)
    {
        for (var index = 0; index < simplex.Count; index++)
        {
            if (simplex.Vertices[index].Point == point)
                return true;
        }

        return false;
    }
}
