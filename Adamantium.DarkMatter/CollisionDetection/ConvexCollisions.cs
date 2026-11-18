using System;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Contacts of any two convex shapes, as Jolt and Bullet find them: GJK on their cores while apart, EPA once
/// they overlap, then a face of one clipping the other's face turned toward it; a single point where no face meets
/// the other flat.</summary>
internal static class ConvexCollisions
{
    /// <summary>How much less than along the true axis the shapes may part along a face's normal for the face to be the
    /// contact's plane, in meters, beside a fiftieth of the separation.</summary>
    private const float FaceTolerance = 0.001f;

    /// <summary>How far cores that only just touch are parted to tell their normal, in sizes of the smaller shape.
    /// </summary>
    private const float PartingNudge = 1e-3f;

    /// <summary>The cosine of the widest turn from the contact normal a face may have to be tried as its plane: about
    /// 25 degrees.</summary>
    private const float NearTheNormal = 0.9f;

    /// <summary>The cosine of the widest turn between two segments for them to lie along each other: about six degrees.
    /// </summary>
    private const float RunningTogether = 0.995f;

    public static bool Collide(CollisionShape first, in Pose firstPose, CollisionShape second, in Pose secondPose,
        float speculativeDistance, ManifoldBuffer manifold)
    {
        var firstCore = new PlacedCore(first, firstPose);
        var secondCore = new PlacedCore(second, secondPose);
        var firstRadius = first.CoreRadius;
        var secondRadius = second.CoreRadius;
        var reach = speculativeDistance + firstRadius + secondRadius;
        Vector3F normal;
        float separation;
        Vector3F onFirst;
        Vector3F onSecond;
        var normalIsKnown = true;
        if (GjkClosestPoints.TryFind(firstCore, secondCore, reach, manifold.Simplex, out var distance, out normal,
                out onFirst, out onSecond))
        {
            separation = distance - firstRadius - secondRadius;
            if (separation > speculativeDistance || !(distance > 0))
                return false;
        }
        else
        {
            var walked = normal;
            if (manifold.Polytope.TryExpand(firstCore, secondCore, manifold.Simplex, out var expanded, out var depth,
                    out var deepestOfFirst, out var deepestOfSecond, out var touching, out var nearTolerance))
            {
                normal = expanded;
                onFirst = deepestOfFirst;
                onSecond = deepestOfSecond;

                // Cores touching edge to edge stand behind several planes, and EPA's may change from step to step;
                // parted a hair, their nearest points tell the one normal.
                if (touching)
                    TryPartToFindNormal(first, firstCore, second, secondPose, nearTolerance, manifold, ref normal,
                        ref onFirst, ref onSecond);
            }
            else
            {
                // Cores EPA could not measure, at a depth not known; GJK's last way is a guess - only the faces can tell.
                normal = walked != Vector3F.Zero ? walked : secondPose.Position - firstPose.Position;
                normal = normal.LengthSquared() > 1e-12f ? Vector3F.Normalize(normal) : Vector3F.UnitY;
                depth = 0;
                normalIsKnown = false;
            }

            separation = -depth - firstRadius - secondRadius;
        }

        // How far the shapes must part along a face's normal for it to be the contact's plane: nearly as far as along
        // the true axis; with no depth measured, any face that parts them best.
        var enough = normalIsKnown ? separation - FaceTolerance - 0.02f * MathF.Abs(separation) : float.MinValue;
        if (!AddFaceContacts(first, firstPose, second, secondPose, normal, normalIsKnown, enough, speculativeDistance,
                manifold)
            && !(normalIsKnown && CanLieAlong(first) && CanLieAlong(second)
                 && AddLineContacts(first, firstPose, second, secondPose, normal, speculativeDistance, manifold)))
        {
            manifold.Normal = normal;
            var surfaceOfFirst = onFirst + normal * firstRadius;
            var surfaceOfSecond = onSecond - normal * secondRadius;
            manifold.Add((surfaceOfFirst + surfaceOfSecond) * 0.5f, separation);
        }

        return true;
    }

    // Parts the second core a hair - a thousandth of the smaller shape, past what EPA counts as touching - along the
    // normal, and takes the way between the nearest points as the normal; left as it is when that parts them not, as
    // far out where the hair is lost. Corner to corner, no way is truer than another, and EPA's stands.
    private static void TryPartToFindNormal(CollisionShape first, in PlacedCore firstCore, CollisionShape second,
        in Pose secondPose, float nearTolerance, ManifoldBuffer manifold, ref Vector3F normal, ref Vector3F onFirst,
        ref Vector3F onSecond)
    {
        var firstSize = Vector3F.Max(first.ComputeBounds(Pose.Identity).Size);
        var secondSize = Vector3F.Max(second.ComputeBounds(Pose.Identity).Size);
        var nudge = MathF.Max(PartingNudge * MathF.Min(firstSize, secondSize), 4 * nearTolerance);
        var offset = normal * nudge;
        var parted = new PlacedCore(second, new Pose(secondPose.Position + offset, secondPose.Rotation));
        if (!GjkClosestPoints.TryFind(firstCore, parted, float.MaxValue, manifold.Simplex, out var distance,
                out var towardSecond, out var nearestOfFirst, out var nearestOfSecond) || !(distance > 0))
            return;

        normal = towardSecond;
        onFirst = nearestOfFirst;
        onSecond = nearestOfSecond - offset;
    }

    // As the separating-axis test does, on the face of each shape turned toward the other: the one along whose normal
    // the shapes part more is the contact's plane when they part along it about as well as along the true axis. It
    // then clips the other shape's face turned toward it, and its normal is the contact's. Otherwise - edge on edge -
    // one point does. A normal not known - cores EPA could not measure - leaves every face to tell.
    private static bool AddFaceContacts(CollisionShape first, in Pose firstPose, CollisionShape second,
        in Pose secondPose, Vector3F normal, bool normalIsKnown, float enough, float speculativeDistance,
        ManifoldBuffer manifold)
    {
        int firstFace;
        int secondFace;
        Vector3F firstNormal;
        Vector3F secondNormal;
        float firstParting;
        float secondParting;
        if (normalIsKnown)
        {
            firstFace = MeasureFace(first, firstPose, second, secondPose, normal, out firstNormal, out firstParting);
            secondFace = MeasureFace(second, secondPose, first, firstPose, -normal, out secondNormal, out secondParting);
        }
        else
        {
            firstFace = secondFace = -1;
            firstNormal = secondNormal = default;
            firstParting = secondParting = float.MinValue;
        }

        if (!normalIsKnown || MathF.Max(firstParting, secondParting) < enough)
        {
            // The faces turned most along the normal do not meet: an edge on an edge, or a face only a little turned
            // from the normal that parts them better - the faces near the normal then tell, or, the normal not known,
            // all of them.
            var nearness = normalIsKnown ? NearTheNormal : -1;
            firstFace = FindPartingFace(first, firstPose, second, secondPose, normal, nearness, out firstNormal,
                out firstParting);
            secondFace = FindPartingFace(second, secondPose, first, firstPose, -normal, nearness, out secondNormal,
                out secondParting);
        }

        var referenceIsFirst = firstFace >= 0 && (secondFace < 0 || !AxisMath.IsClearlyGreater(secondParting, firstParting));
        if (!referenceIsFirst && secondFace < 0)
            return false;

        var parting = referenceIsFirst ? firstParting : secondParting;
        if (parting < enough)
            return false;

        var referenceShape = referenceIsFirst ? first : second;
        var referencePose = referenceIsFirst ? firstPose : secondPose;
        var incidentShape = referenceIsFirst ? second : first;
        var incidentPose = referenceIsFirst ? secondPose : firstPose;
        var faceNormal = referenceIsFirst ? firstNormal : secondNormal;
        var reference = manifold.FaceFirst;
        var incident = manifold.FaceSecond;
        var referenceCount = referenceShape.GetFlatFace(referenceIsFirst ? firstFace : secondFace, reference);
        for (var index = 0; index < referenceCount; index++)
        {
            reference[index] = referencePose.TransformPoint(reference[index]);
        }

        var incidentCount = PlaceFace(incidentShape, incidentPose, -faceNormal, incident);
        var referenceRadius = referenceShape.CoreRadius;
        var incidentRadius = incidentShape.CoreRadius;

        // The plane the parting was measured from: through the face's farthest corner.
        var plane = Vector3F.Dot(faceNormal, referencePose.Position)
                    + referenceShape.GetFlatFaceOffset(referenceIsFirst ? firstFace : secondFace);
        var count = ClipByFace(reference, referenceCount, faceNormal, incident, incidentCount, manifold);
        var clipped = manifold.ClipFirst;
        var start = manifold.Count;
        for (var index = 0; index < count; index++)
        {
            var point = clipped[index];
            var beyond = Vector3F.Dot(point, faceNormal) - plane;
            var pointSeparation = beyond - referenceRadius - incidentRadius;
            if (pointSeparation > speculativeDistance)
                continue;

            var onReference = point - faceNormal * (beyond - referenceRadius);
            var onIncident = point - faceNormal * incidentRadius;
            manifold.Add((onReference + onIncident) * 0.5f, pointSeparation);
        }

        if (manifold.Count == start)
            return false;

        manifold.Normal = referenceIsFirst ? faceNormal : -faceNormal;
        return true;
    }

    // Two sides lying along each other - logs, a log and a capsule - touch along a line: the ends of where their
    // segments overlap, each a point; false unless both give a segment and the two run together.
    private static bool AddLineContacts(CollisionShape first, in Pose firstPose, CollisionShape second,
        in Pose secondPose, Vector3F normal, float speculativeDistance, ManifoldBuffer manifold)
    {
        if (PlaceFace(first, firstPose, normal, manifold.FaceFirst) != 2
            || PlaceFace(second, secondPose, -normal, manifold.FaceSecond) != 2)
            return false;

        var start = manifold.FaceFirst[0];
        var along = manifold.FaceFirst[1] - start;
        var length = along.Length();
        var otherStart = manifold.FaceSecond[0];
        var otherAlong = manifold.FaceSecond[1] - otherStart;
        var otherLength = otherAlong.Length();
        if (!(length > 0) || !(otherLength > 0))
            return false;

        along /= length;
        if (MathF.Abs(Vector3F.Dot(along, otherAlong / otherLength)) < RunningTogether)
            return false;

        var otherFrom = Vector3F.Dot(otherStart - start, along);
        var otherTo = Vector3F.Dot(manifold.FaceSecond[1] - start, along);
        var from = MathF.Max(0, MathF.Min(otherFrom, otherTo));
        var to = MathF.Min(length, MathF.Max(otherFrom, otherTo));
        if (from > to)
            return false;

        var firstRadius = first.CoreRadius;
        var secondRadius = second.CoreRadius;
        var added = manifold.Count;
        var ends = to - from > 1e-6f * length ? 2 : 1;
        foreach (var at in ((ReadOnlySpan<float>)[from, to])[..ends])
        {
            var onFirst = start + along * at;
            var onSecond = otherStart + otherAlong * Math.Clamp(Vector3F.Dot(onFirst - otherStart, otherAlong)
                                                                / (otherLength * otherLength), 0, 1);
            var separation = Vector3F.Dot(onSecond - onFirst, normal) - firstRadius - secondRadius;
            if (separation <= speculativeDistance)
                manifold.Add((onFirst + normal * firstRadius + onSecond - normal * secondRadius) * 0.5f, separation);
        }

        if (manifold.Count == added)
            return false;

        manifold.Normal = normal;
        return true;
    }

    // Shapes whose side is a segment: only they can lie along each other.
    private static bool CanLieAlong(CollisionShape shape) =>
        shape.Kind is CollisionShapeKind.Capsule or CollisionShapeKind.Cylinder;

    // Of the faces of shape whose normal turns toward toward by at least the cosine nearness, the one whose outward
    // normal parts it most from other, that normal in the world and how far they part along it; -1 when it has none
    // such.
    private static int FindPartingFace(CollisionShape shape, in Pose pose, CollisionShape other, in Pose otherPose,
        Vector3F toward, float nearness, out Vector3F faceNormal, out float parting)
    {
        faceNormal = default;
        parting = float.MinValue;
        var best = -1;
        var localToward = pose.InverseTransformDirection(toward);
        for (var face = 0; face < shape.FlatFaceCount; face++)
        {
            var localNormal = shape.GetFlatFaceNormal(face);
            if (Vector3F.Dot(localNormal, localToward) < nearness)
                continue;

            var axis = pose.TransformDirection(localNormal);
            var apart = MeasureParting(shape, pose, face, other, otherPose, axis);
            if (apart > parting)
            {
                best = face;
                parting = apart;
                faceNormal = axis;
            }
        }

        return best;
    }

    // How far the other shape stands beyond the face, along its outward normal axis - the core's plane out by the
    // shape's radius; less than zero while it reaches behind it.
    private static float MeasureParting(CollisionShape shape, in Pose pose, int face, CollisionShape other,
        in Pose otherPose, Vector3F axis)
    {
        var nearestOfOther = otherPose.TransformPoint(other.GetSupport(otherPose.InverseTransformDirection(-axis)));
        return Vector3F.Dot(nearestOfOther - pose.Position, axis) - shape.GetFlatFaceOffset(face) - shape.CoreRadius;
    }

    // The face of shape turned most toward the other along toward, its outward normal in the world and how far the
    // shapes part along it; -1 for a shape without faces.
    private static int MeasureFace(CollisionShape shape, in Pose pose, CollisionShape other, in Pose otherPose,
        Vector3F toward, out Vector3F faceNormal, out float parting)
    {
        faceNormal = default;
        parting = float.MinValue;
        var face = shape.FindFlatFace(pose.InverseTransformDirection(toward));
        if (face < 0)
            return -1;

        faceNormal = pose.TransformDirection(shape.GetFlatFaceNormal(face));
        parting = MeasureParting(shape, pose, face, other, otherPose, faceNormal);
        return face;
    }

    private static int PlaceFace(CollisionShape shape, in Pose pose, Vector3F direction, Vector3F[] into)
    {
        var count = shape.GetSupportingFace(pose.InverseTransformDirection(direction), into);
        for (var index = 0; index < count; index++)
        {
            into[index] = pose.TransformPoint(into[index]);
        }

        return count;
    }

    // Clips the incident points - a polygon, an edge or a point - by the planes through the reference polygon's edges.
    private static int ClipByFace(Vector3F[] reference, int referenceCount, Vector3F faceNormal, Vector3F[] incident,
        int incidentCount, ManifoldBuffer manifold)
    {
        var polygon = manifold.ClipFirst;
        var spare = manifold.ClipSecond;
        Array.Copy(incident, polygon, incidentCount);
        var count = incidentCount;
        var center = Vector3F.Zero;
        for (var index = 0; index < referenceCount; index++)
        {
            center += reference[index];
        }

        center /= referenceCount;
        for (var edge = 0; edge < referenceCount && count > 0; edge++)
        {
            var from = reference[edge];
            var to = reference[(edge + 1) % referenceCount];
            var side = Vector3F.Cross(to - from, faceNormal);
            var length = side.Length();
            if (!(length > 0))
                continue;

            side /= length;
            if (Vector3F.Dot(side, center - from) > 0)
                side = -side;

            count = ClipBySide(polygon, count, spare, side, Vector3F.Dot(side, from));
            var clipped = spare;
            spare = polygon;
            polygon = clipped;
        }

        if (!ReferenceEquals(polygon, manifold.ClipFirst))
            Array.Copy(polygon, manifold.ClipFirst, count);

        return count;
    }

    // What of the points lies behind the plane: a polygon's corners and crossings, an edge cut short, a point or none.
    private static int ClipBySide(Vector3F[] input, int count, Vector3F[] output, Vector3F normal, float limit)
    {
        if (count == 1)
        {
            if (Vector3F.Dot(normal, input[0]) - limit > 0)
                return 0;

            output[0] = input[0];
            return 1;
        }

        if (count == 2)
        {
            var start = input[0];
            var end = input[1];
            var startDistance = Vector3F.Dot(normal, start) - limit;
            var endDistance = Vector3F.Dot(normal, end) - limit;
            if (startDistance > 0 && endDistance > 0)
                return 0;

            if (startDistance > 0)
                start += (end - start) * (startDistance / (startDistance - endDistance));
            else if (endDistance > 0)
                end += (start - end) * (endDistance / (endDistance - startDistance));

            output[0] = start;
            output[1] = end;
            return 2;
        }

        return PolygonClipping.ClipBehind(input, count, output, normal, limit);
    }
}
