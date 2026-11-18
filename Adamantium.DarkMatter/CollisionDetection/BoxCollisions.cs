using System;
using Adamantium.DarkMatter.Shapes;
using static Adamantium.DarkMatter.CollisionDetection.AxisMath;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Contacts of two boxes: the axis of least overlap among the fifteen that can part them, then a face clipped
/// by a face or the nearest points of two edges.</summary>
internal static class BoxCollisions
{
    private const float ParallelEdgeLength = 1e-5f;

    public static bool CollideBoxes(BoxCollisionShape first, in Pose firstPose, BoxCollisionShape second,
        in Pose secondPose, float speculativeDistance, ManifoldBuffer manifold)
    {
        Span<Vector3F> firstAxes = stackalloc Vector3F[3];
        Span<Vector3F> secondAxes = stackalloc Vector3F[3];
        for (var axis = 0; axis < 3; axis++)
        {
            firstAxes[axis] = firstPose.TransformDirection(GetUnitAxis(axis));
            secondAxes[axis] = secondPose.TransformDirection(GetUnitAxis(axis));
        }

        var firstSize = first.HalfExtents;
        var secondSize = second.HalfExtents;
        var between = secondPose.Position - firstPose.Position;

        var firstFaceAxis = 0;
        var firstFaceSeparation = float.MinValue;
        for (var axis = 0; axis < 3; axis++)
        {
            var direction = firstAxes[axis];
            var separation = MathF.Abs(Vector3F.Dot(between, direction)) - GetComponent(firstSize, axis)
                             - MeasureReach(secondAxes, secondSize, direction);
            if (separation > speculativeDistance)
                return false;

            if (separation > firstFaceSeparation)
            {
                firstFaceSeparation = separation;
                firstFaceAxis = axis;
            }
        }

        var secondFaceAxis = 0;
        var secondFaceSeparation = float.MinValue;
        for (var axis = 0; axis < 3; axis++)
        {
            var direction = secondAxes[axis];
            var separation = MathF.Abs(Vector3F.Dot(between, direction)) - GetComponent(secondSize, axis)
                             - MeasureReach(firstAxes, firstSize, direction);
            if (separation > speculativeDistance)
                return false;

            if (separation > secondFaceSeparation)
            {
                secondFaceSeparation = separation;
                secondFaceAxis = axis;
            }
        }

        var edgeFirstAxis = -1;
        var edgeSecondAxis = -1;
        var edgeSeparation = float.MinValue;
        var edgeDirection = Vector3F.Zero;
        for (var firstAxis = 0; firstAxis < 3; firstAxis++)
        {
            for (var secondAxis = 0; secondAxis < 3; secondAxis++)
            {
                var direction = Vector3F.Cross(firstAxes[firstAxis], secondAxes[secondAxis]);
                var length = direction.Length();
                if (length < ParallelEdgeLength)
                    continue;

                direction /= length;
                var separation = MathF.Abs(Vector3F.Dot(between, direction)) - MeasureReach(firstAxes, firstSize, direction)
                                 - MeasureReach(secondAxes, secondSize, direction);
                if (separation > speculativeDistance)
                    return false;

                if (separation > edgeSeparation)
                {
                    edgeSeparation = separation;
                    edgeFirstAxis = firstAxis;
                    edgeSecondAxis = secondAxis;
                    edgeDirection = direction;
                }
            }
        }

        var referenceIsFirst = true;
        var faceSeparation = firstFaceSeparation;
        if (IsClearlyGreater(secondFaceSeparation, firstFaceSeparation))
        {
            referenceIsFirst = false;
            faceSeparation = secondFaceSeparation;
        }

        if (edgeFirstAxis >= 0 && IsClearlyGreater(edgeSeparation, faceSeparation))
        {
            if (Vector3F.Dot(between, edgeDirection) < 0)
                edgeDirection = -edgeDirection;

            AddEdgeContact(firstPose.Position, firstAxes, firstSize, edgeFirstAxis, secondPose.Position, secondAxes,
                secondSize, edgeSecondAxis, edgeDirection, manifold);
            return true;
        }

        if (referenceIsFirst)
        {
            ClipFaces(firstPose.Position, firstAxes, firstSize, firstFaceAxis, secondPose.Position, secondAxes,
                secondSize, speculativeDistance, manifold);
        }
        else
        {
            ClipFaces(secondPose.Position, secondAxes, secondSize, secondFaceAxis, firstPose.Position, firstAxes,
                firstSize, speculativeDistance, manifold);
            manifold.Normal = -manifold.Normal;
        }

        return manifold.Count > 0;
    }

    private static float MeasureReach(ReadOnlySpan<Vector3F> axes, Vector3F size, Vector3F direction) =>
        size.X * MathF.Abs(Vector3F.Dot(axes[0], direction)) + size.Y * MathF.Abs(Vector3F.Dot(axes[1], direction))
                                                              + size.Z * MathF.Abs(Vector3F.Dot(axes[2], direction));

    private static void ClipFaces(Vector3F referenceCenter, ReadOnlySpan<Vector3F> referenceAxes, Vector3F referenceSize,
        int referenceAxis, Vector3F incidentCenter, ReadOnlySpan<Vector3F> incidentAxes, Vector3F incidentSize,
        float speculativeDistance, ManifoldBuffer manifold)
    {
        var referenceNormal = referenceAxes[referenceAxis];
        if (Vector3F.Dot(incidentCenter - referenceCenter, referenceNormal) < 0)
            referenceNormal = -referenceNormal;

        var incidentAxis = 0;
        var bestAlignment = -1f;
        for (var axis = 0; axis < 3; axis++)
        {
            var alignment = MathF.Abs(Vector3F.Dot(referenceNormal, incidentAxes[axis]));
            if (alignment > bestAlignment)
            {
                bestAlignment = alignment;
                incidentAxis = axis;
            }
        }

        var facing = Vector3F.Dot(referenceNormal, incidentAxes[incidentAxis]) > 0 ? -1f : 1f;
        var faceCenter = incidentCenter + incidentAxes[incidentAxis] * (facing * GetComponent(incidentSize, incidentAxis));
        var acrossAxis = (incidentAxis + 1) % 3;
        var downAxis = (incidentAxis + 2) % 3;
        var across = incidentAxes[acrossAxis] * GetComponent(incidentSize, acrossAxis);
        var down = incidentAxes[downAxis] * GetComponent(incidentSize, downAxis);

        var polygon = manifold.ClipFirst;
        var spare = manifold.ClipSecond;
        polygon[0] = faceCenter + across + down;
        polygon[1] = faceCenter - across + down;
        polygon[2] = faceCenter - across - down;
        polygon[3] = faceCenter + across - down;
        var count = 4;

        for (var axis = 0; axis < 3 && count > 0; axis++)
        {
            if (axis == referenceAxis)
                continue;

            var side = referenceAxes[axis];
            var offset = Vector3F.Dot(side, referenceCenter);
            var limit = GetComponent(referenceSize, axis);
            count = PolygonClipping.ClipBehind(polygon, count, spare, side, offset + limit);
            count = PolygonClipping.ClipBehind(spare, count, polygon, -side, limit - offset);
        }

        var faceDistance = Vector3F.Dot(referenceNormal, referenceCenter) + GetComponent(referenceSize, referenceAxis);
        manifold.Normal = referenceNormal;
        for (var index = 0; index < count; index++)
        {
            var point = polygon[index];
            var separation = Vector3F.Dot(referenceNormal, point) - faceDistance;
            if (separation <= speculativeDistance)
                manifold.Add(point - referenceNormal * (separation * 0.5f), separation);
        }
    }

    private static void AddEdgeContact(Vector3F firstCenter, ReadOnlySpan<Vector3F> firstAxes, Vector3F firstSize,
        int firstAxis, Vector3F secondCenter, ReadOnlySpan<Vector3F> secondAxes, Vector3F secondSize, int secondAxis,
        Vector3F normal, ManifoldBuffer manifold)
    {
        var firstEdge = firstCenter;
        var secondEdge = secondCenter;
        for (var axis = 0; axis < 3; axis++)
        {
            if (axis != firstAxis)
            {
                var toward = Vector3F.Dot(normal, firstAxes[axis]) >= 0 ? 1f : -1f;
                firstEdge += firstAxes[axis] * (toward * GetComponent(firstSize, axis));
            }

            if (axis != secondAxis)
            {
                var toward = Vector3F.Dot(normal, secondAxes[axis]) >= 0 ? -1f : 1f;
                secondEdge += secondAxes[axis] * (toward * GetComponent(secondSize, axis));
            }
        }

        var firstHalf = firstAxes[firstAxis] * GetComponent(firstSize, firstAxis);
        var secondHalf = secondAxes[secondAxis] * GetComponent(secondSize, secondAxis);
        Collision.FindClosestPointsOnSegments(firstEdge - firstHalf, firstEdge + firstHalf, secondEdge - secondHalf,
            secondEdge + secondHalf, out var onFirst, out var onSecond);
        manifold.Normal = normal;
        manifold.Add((onFirst + onSecond) * 0.5f, Vector3F.Dot(onSecond - onFirst, normal));
    }
}
