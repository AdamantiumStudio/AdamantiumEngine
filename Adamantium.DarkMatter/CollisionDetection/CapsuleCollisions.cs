using System;
using Adamantium.DarkMatter.Shapes;
using static Adamantium.DarkMatter.CollisionDetection.AxisMath;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Contacts of a capsule with a capsule and a box.</summary>
internal static class CapsuleCollisions
{
    private const float CoincidentDistance = 1e-5f;
    private const float FaceAlignment = 0.98f;
    private const int SearchSteps = 40;
    private const float GoldenRatio = 0.618034f;

    public static bool CollideCapsules(CapsuleCollisionShape first, in Pose firstPose, CapsuleCollisionShape second,
        in Pose secondPose, float speculativeDistance, ManifoldBuffer manifold)
    {
        var firstBottom = firstPose.TransformPoint(new Vector3F(0, -first.HalfLength, 0));
        var firstTop = firstPose.TransformPoint(new Vector3F(0, first.HalfLength, 0));
        var secondBottom = secondPose.TransformPoint(new Vector3F(0, -second.HalfLength, 0));
        var secondTop = secondPose.TransformPoint(new Vector3F(0, second.HalfLength, 0));
        Collision.FindClosestPointsOnSegments(firstBottom, firstTop, secondBottom, secondTop, out var onFirst,
            out var onSecond);

        var radii = first.Radius + second.Radius;
        var between = onSecond - onFirst;
        var distance = between.Length();
        if (distance - radii > speculativeDistance)
            return false;

        var firstAlong = firstTop - firstBottom;
        var secondAlong = secondTop - secondBottom;
        var cross = Vector3F.Cross(firstAlong, secondAlong);
        Vector3F normal;
        if (distance > CoincidentDistance)
        {
            normal = between / distance;
        }
        else
        {
            normal = cross.LengthSquared() > 1e-12f ? Vector3F.Normalize(cross) : GetPerpendicular(firstAlong);
            if (Vector3F.Dot(normal, secondPose.Position - firstPose.Position) < 0)
                normal = -normal;
        }

        if (LineContacts.TryAdd(firstBottom, firstTop, secondBottom, secondTop, normal, first.Radius, second.Radius,
                speculativeDistance, manifold))
            return true;

        manifold.Normal = normal;
        var surfaceFirst = onFirst + normal * first.Radius;
        var surfaceSecond = onSecond - normal * second.Radius;
        manifold.Add((surfaceFirst + surfaceSecond) * 0.5f, Vector3F.Dot(between, normal) - radii);
        return true;
    }

    public static bool CollideCapsuleBox(CapsuleCollisionShape capsule, in Pose capsulePose, BoxCollisionShape box,
        in Pose boxPose, float speculativeDistance, ManifoldBuffer manifold)
    {
        var size = box.HalfExtents;
        var radius = capsule.Radius;
        var worldStart = capsulePose.TransformPoint(new Vector3F(0, -capsule.HalfLength, 0));
        var worldEnd = capsulePose.TransformPoint(new Vector3F(0, capsule.HalfLength, 0));
        var start = boxPose.InverseTransformPoint(worldStart);
        var end = boxPose.InverseTransformPoint(worldEnd);
        var along = end - start;

        var amount = FindNearestAmount(start, along, size);
        var nearest = start + along * amount;
        var onBox = Vector3F.Clamp(nearest, -size, size);
        var distance = (onBox - nearest).Length();

        if (distance > CoincidentDistance)
        {
            var localNormal = (onBox - nearest) / distance;
            if (distance - radius > speculativeDistance)
                return false;

            var faceAxis = FindDominantAxis(localNormal);
            if (MathF.Abs(GetComponent(localNormal, faceAxis)) > FaceAlignment)
            {
                var faceSign = GetComponent(localNormal, faceAxis) > 0 ? -1f : 1f;
                if (ClipAgainstFace(start, along, size, faceAxis, faceSign, radius, speculativeDistance, boxPose,
                        manifold))
                    return true;
            }

            // A capsule lying along an edge of the box touches along it, found as the general path finds it.
            if (LineContacts.TryAddAlongEdge(worldStart, worldEnd, capsule.Radius, box, boxPose,
                    boxPose.TransformDirection(localNormal), speculativeDistance, manifold))
                return true;

            manifold.Normal = boxPose.TransformDirection(localNormal);
            var onCapsule = nearest + localNormal * radius;
            manifold.Add(boxPose.TransformPoint((onCapsule + onBox) * 0.5f), distance - radius);
            return true;
        }

        return CollideCoreInsideBox(start, along, size, radius, speculativeDistance, boxPose, manifold);
    }

    private static float FindNearestAmount(Vector3F start, Vector3F along, Vector3F size)
    {
        var low = 0f;
        var high = 1f;
        var lowProbe = high - GoldenRatio * (high - low);
        var highProbe = low + GoldenRatio * (high - low);
        var lowDistance = DistanceSquaredToBox(start + along * lowProbe, size);
        var highDistance = DistanceSquaredToBox(start + along * highProbe, size);
        for (var step = 0; step < SearchSteps; step++)
        {
            if (lowDistance <= highDistance)
            {
                high = highProbe;
                highProbe = lowProbe;
                highDistance = lowDistance;
                lowProbe = high - GoldenRatio * (high - low);
                lowDistance = DistanceSquaredToBox(start + along * lowProbe, size);
            }
            else
            {
                low = lowProbe;
                lowProbe = highProbe;
                lowDistance = highDistance;
                highProbe = low + GoldenRatio * (high - low);
                highDistance = DistanceSquaredToBox(start + along * highProbe, size);
            }
        }

        var middle = (low + high) * 0.5f;
        var best = middle;
        var bestDistance = DistanceSquaredToBox(start + along * middle, size);
        if (DistanceSquaredToBox(start, size) < bestDistance)
        {
            best = 0;
            bestDistance = DistanceSquaredToBox(start, size);
        }

        if (DistanceSquaredToBox(start + along, size) < bestDistance)
            best = 1;

        return best;
    }

    private static float DistanceSquaredToBox(Vector3F point, Vector3F size) =>
        (point - Vector3F.Clamp(point, -size, size)).LengthSquared();

    private static bool CollideCoreInsideBox(Vector3F start, Vector3F along, Vector3F size, float radius,
        float speculativeDistance, in Pose boxPose, ManifoldBuffer manifold)
    {
        var bestFaceAxis = 0;
        var bestFaceSeparation = float.MinValue;
        var bestFaceSign = 1f;
        for (var axis = 0; axis < 3; axis++)
        {
            var direction = GetUnitAxis(axis);
            var separation = MeasureSeparation(start, along, size, direction, radius, out var side);
            if (separation > bestFaceSeparation)
            {
                bestFaceSeparation = separation;
                bestFaceAxis = axis;
                bestFaceSign = side;
            }
        }

        var bestEdgeAxis = -1;
        var bestEdgeSeparation = float.MinValue;
        var bestEdgeDirection = Vector3F.Zero;
        var bestEdgeSign = 1f;
        for (var axis = 0; axis < 3; axis++)
        {
            var direction = Vector3F.Cross(GetUnitAxis(axis), along);
            var length = direction.Length();
            if (length < 1e-5f)
                continue;

            direction /= length;
            var separation = MeasureSeparation(start, along, size, direction, radius, out var side);
            if (separation > bestEdgeSeparation)
            {
                bestEdgeSeparation = separation;
                bestEdgeAxis = axis;
                bestEdgeDirection = direction;
                bestEdgeSign = side;
            }
        }

        var useEdge = bestEdgeAxis >= 0 && IsClearlyGreater(bestEdgeSeparation, bestFaceSeparation);
        if (!useEdge)
        {
            if (bestFaceSeparation > speculativeDistance)
                return false;

            if (ClipAgainstFace(start, along, size, bestFaceAxis, bestFaceSign, radius, speculativeDistance, boxPose,
                    manifold))
                return true;

            var faceNormal = GetUnitAxis(bestFaceAxis) * bestFaceSign;
            var middle = start + along * 0.5f;
            manifold.Normal = boxPose.TransformDirection(-faceNormal);
            manifold.Add(boxPose.TransformPoint(middle), bestFaceSeparation);
            return true;
        }

        if (bestEdgeSeparation > speculativeDistance)
            return false;

        var toCapsule = bestEdgeDirection * bestEdgeSign;
        var edgeCenter = Vector3F.Zero;
        for (var axis = 0; axis < 3; axis++)
        {
            if (axis == bestEdgeAxis)
                continue;

            var component = GetComponent(toCapsule, axis) >= 0 ? GetComponent(size, axis) : -GetComponent(size, axis);
            edgeCenter += GetUnitAxis(axis) * component;
        }

        var edgeHalf = GetUnitAxis(bestEdgeAxis) * GetComponent(size, bestEdgeAxis);
        Collision.FindClosestPointsOnSegments(start, start + along, edgeCenter - edgeHalf, edgeCenter + edgeHalf,
            out var onSegment, out var onEdge);
        var normal = -toCapsule;
        var onCapsule = onSegment + normal * radius;
        manifold.Normal = boxPose.TransformDirection(normal);
        manifold.Add(boxPose.TransformPoint((onCapsule + onEdge) * 0.5f), bestEdgeSeparation);
        return true;
    }

    private static float MeasureSeparation(Vector3F start, Vector3F along, Vector3F size, Vector3F direction,
        float radius, out float side)
    {
        var boxReach = MathF.Abs(direction.X) * size.X + MathF.Abs(direction.Y) * size.Y + MathF.Abs(direction.Z) * size.Z;
        var startProjection = Vector3F.Dot(start, direction);
        var endProjection = Vector3F.Dot(start + along, direction);
        var low = Math.Min(startProjection, endProjection);
        var high = Math.Max(startProjection, endProjection);
        side = startProjection + endProjection >= 0 ? 1f : -1f;
        return Math.Max(low - boxReach, -boxReach - high) - radius;
    }

    private static bool ClipAgainstFace(Vector3F start, Vector3F along, Vector3F size, int faceAxis, float faceSign,
        float radius, float speculativeDistance, in Pose boxPose, ManifoldBuffer manifold)
    {
        var low = 0f;
        var high = 1f;
        for (var axis = 0; axis < 3; axis++)
        {
            if (axis == faceAxis)
                continue;

            var origin = GetComponent(start, axis);
            var step = GetComponent(along, axis);
            var limit = GetComponent(size, axis);
            if (MathF.Abs(step) < 1e-9f)
            {
                if (origin < -limit || origin > limit)
                    return false;

                continue;
            }

            var first = (-limit - origin) / step;
            var second = (limit - origin) / step;
            low = Math.Max(low, Math.Min(first, second));
            high = Math.Min(high, Math.Max(first, second));
        }

        if (low > high)
            return false;

        var faceNormal = GetUnitAxis(faceAxis) * faceSign;
        var faceDistance = GetComponent(size, faceAxis);
        var countBefore = manifold.Count;
        AddFacePoint(start + along * low, faceNormal, faceDistance, radius, speculativeDistance, boxPose, manifold);
        if (high - low > 1e-4f)
            AddFacePoint(start + along * high, faceNormal, faceDistance, radius, speculativeDistance, boxPose, manifold);

        if (manifold.Count == countBefore)
            return false;

        manifold.Normal = boxPose.TransformDirection(-faceNormal);
        return true;
    }

    private static void AddFacePoint(Vector3F point, Vector3F faceNormal, float faceDistance, float radius,
        float speculativeDistance, in Pose boxPose, ManifoldBuffer manifold)
    {
        var height = Vector3F.Dot(point, faceNormal) - faceDistance;
        var separation = height - radius;
        if (separation > speculativeDistance)
            return;

        var onCapsule = point - faceNormal * radius;
        var onFace = point - faceNormal * height;
        manifold.Add(boxPose.TransformPoint((onCapsule + onFace) * 0.5f), separation);
    }

}
