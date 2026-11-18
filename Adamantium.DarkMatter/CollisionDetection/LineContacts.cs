using System;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Contacts along a line: a segment lying along another, or along an edge of a face, touches at both ends of
/// where the two overlap, not at one point it could turn about.</summary>
internal static class LineContacts
{
    // The cosine of the widest turn between a segment and a segment or an edge for them to lie along each other: about
    // six degrees.
    private const float RunningTogether = 0.995f;

    // How far, in meters, an edge's higher end may stand below its face's corner nearest the other, and how much an
    // overlap's end standing aside may misstate its depth: fixed, not grown with speed.
    private const float LineTolerance = 0.005f;

    // The least overlap, as a share of the line, that gives two points rather than one.
    private const float LeastOverlap = 1e-3f;

    /// <summary>A segment - a capsule's or a cylinder's side - lying along an edge of <paramref name="shape"/>'s face
    /// toward it: adds the line and gives true. <paramref name="normal"/> runs from the segment to the shape.</summary>
    public static bool TryAddAlongEdge(Vector3F start, Vector3F end, float segmentRadius, CollisionShape shape,
        in Pose pose, Vector3F normal, float speculativeDistance, ManifoldBuffer manifold)
    {
        var count = shape.PlaceSupportingFace(pose, -normal, manifold.FaceSecond);
        return TryFindEdgeAlong(manifold.FaceSecond, count, -normal, end - start, out var edgeStart, out var edgeEnd)
               && TryAdd(start, end, edgeStart, edgeEnd, normal, segmentRadius, shape.CoreRadius, speculativeDistance,
                   manifold);
    }

    // Of a face's edges running with way, the one standing farthest along toward, its higher end no more than a few
    // millimeters below the face's farthest corner - where the face meets the other, not a lower edge beside a corner
    // that does; false when there is none.
    private static bool TryFindEdgeAlong(Vector3F[] corners, int count, Vector3F toward, Vector3F way,
        out Vector3F edgeStart, out Vector3F edgeEnd)
    {
        edgeStart = default;
        edgeEnd = default;
        if (count < 3)
            return false;

        var farthest = float.MinValue;
        for (var corner = 0; corner < count; corner++)
        {
            farthest = MathF.Max(farthest, Vector3F.Dot(corners[corner], toward));
        }

        var wayLengthSquared = way.LengthSquared();
        var found = false;
        var reach = float.MinValue;
        var higherEnd = float.MinValue;
        for (var corner = 0; corner < count; corner++)
        {
            var from = corners[corner];
            var to = corners[(corner + 1) % count];
            if (!RunTogether(to - from, way, wayLengthSquared))
                continue;

            var fromReach = Vector3F.Dot(from, toward);
            var toReach = Vector3F.Dot(to, toward);
            var along = (fromReach + toReach) * 0.5f;
            if (along > reach)
            {
                reach = along;
                higherEnd = MathF.Max(fromReach, toReach);
                edgeStart = from;
                edgeEnd = to;
                found = true;
            }
        }

        return found && higherEnd >= farthest - LineTolerance;
    }

    /// <summary>Adds the ends of where the first line overlaps the second, each its gap along
    /// <paramref name="normal"/> - from the first shape to the second - less the shapes' radii, and sets the normal;
    /// false when the lines do not run together or overlap, an end stands so far aside from the other line that its
    /// depth would be misstated by more than a few millimeters - they cross, not lie along - or no end comes near
    /// enough.</summary>
    public static bool TryAdd(Vector3F start, Vector3F end, Vector3F otherStart, Vector3F otherEnd, Vector3F normal,
        float firstRadius, float secondRadius, float speculativeDistance, ManifoldBuffer manifold)
    {
        var along = end - start;
        var lengthSquared = along.LengthSquared();
        var otherAlong = otherEnd - otherStart;
        var otherLengthSquared = otherAlong.LengthSquared();
        if (!(lengthSquared > 0) || !(otherLengthSquared > 0) || !RunTogether(along, otherAlong, otherLengthSquared))
            return false;

        var length = MathF.Sqrt(lengthSquared);
        along /= length;
        var otherFrom = Vector3F.Dot(otherStart - start, along);
        var otherTo = Vector3F.Dot(otherEnd - start, along);
        var from = MathF.Max(0, MathF.Min(otherFrom, otherTo));
        var to = MathF.Min(length, MathF.Max(otherFrom, otherTo));
        if (from > to)
            return false;

        // Standing aside by l, rounded surfaces of radii summing to R meet about l² / 2R lower than measured along the
        // normal: aside so far that this passes the tolerance, the lines cross.
        var roundness = MathF.Max(firstRadius + secondRadius, LineTolerance);
        var furthestAsideSquared = 2 * roundness * LineTolerance;
        Span<Vector3F> onFirst = stackalloc Vector3F[2];
        Span<Vector3F> onSecond = stackalloc Vector3F[2];
        var ends = to - from > LeastOverlap * length ? 2 : 1;
        for (var index = 0; index < ends; index++)
        {
            onFirst[index] = start + along * (index == 0 ? from : to);
            onSecond[index] = otherStart + otherAlong * Math.Clamp(
                Vector3F.Dot(onFirst[index] - otherStart, otherAlong) / otherLengthSquared, 0, 1);
            var between = onSecond[index] - onFirst[index];
            if ((between - normal * Vector3F.Dot(between, normal)).LengthSquared() > furthestAsideSquared)
                return false;
        }

        var added = manifold.Count;
        for (var index = 0; index < ends; index++)
        {
            var separation = Vector3F.Dot(onSecond[index] - onFirst[index], normal) - firstRadius - secondRadius;
            if (separation <= speculativeDistance)
                manifold.Add((onFirst[index] + normal * firstRadius + onSecond[index] - normal * secondRadius) * 0.5f,
                    separation);
        }

        if (manifold.Count == added)
            return false;

        manifold.Normal = normal;
        return true;
    }

    // Whether two directions, neither of no length, run together within about six degrees, either way round; compared
    // squared, no roots.
    private static bool RunTogether(Vector3F first, Vector3F second, float secondLengthSquared)
    {
        var firstLengthSquared = first.LengthSquared();
        var dot = Vector3F.Dot(first, second);
        return firstLengthSquared > 0 && secondLengthSquared > 0
               && dot * dot >= RunningTogether * RunningTogether * firstLengthSquared * secondLengthSquared;
    }
}
