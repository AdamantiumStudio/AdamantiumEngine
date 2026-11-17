using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Contacts of a sphere with a sphere, a capsule and a box.</summary>
internal static class SphereCollisions
{
    private const float CoincidentDistance = 1e-6f;

    /// <summary>Two balls; the fallback normal is used when their centers coincide.</summary>
    public static bool CollideBalls(Vector3F firstCenter, float firstRadius, Vector3F secondCenter, float secondRadius,
        Vector3F fallbackNormal, float speculativeDistance, ManifoldBuffer manifold)
    {
        var between = secondCenter - firstCenter;
        var distance = between.Length();
        var separation = distance - firstRadius - secondRadius;
        if (separation > speculativeDistance)
            return false;

        var normal = distance > CoincidentDistance ? between / distance : fallbackNormal;
        var onFirst = firstCenter + normal * firstRadius;
        var onSecond = secondCenter - normal * secondRadius;
        manifold.Normal = normal;
        manifold.Add((onFirst + onSecond) * 0.5f, separation);
        return true;
    }

    public static bool CollideSpheres(SphereCollisionShape first, in Pose firstPose, SphereCollisionShape second,
        in Pose secondPose, float speculativeDistance, ManifoldBuffer manifold) =>
        CollideBalls(firstPose.Position, first.Radius, secondPose.Position, second.Radius, Vector3F.UnitY,
            speculativeDistance, manifold);

    public static bool CollideSphereCapsule(SphereCollisionShape sphere, in Pose spherePose,
        CapsuleCollisionShape capsule, in Pose capsulePose, float speculativeDistance, ManifoldBuffer manifold)
    {
        var top = capsulePose.TransformPoint(new Vector3F(0, capsule.HalfLength, 0));
        var bottom = capsulePose.TransformPoint(new Vector3F(0, -capsule.HalfLength, 0));
        var nearest = Collision.ClosestPointSegmentPoint(bottom, top, spherePose.Position);
        var fallback = AxisMath.GetPerpendicular(top - bottom);
        return CollideBalls(spherePose.Position, sphere.Radius, nearest, capsule.Radius, fallback, speculativeDistance,
            manifold);
    }

    public static bool CollideSphereBox(SphereCollisionShape sphere, in Pose spherePose, BoxCollisionShape box,
        in Pose boxPose, float speculativeDistance, ManifoldBuffer manifold)
    {
        var size = box.HalfExtents;
        var center = boxPose.InverseTransformPoint(spherePose.Position);
        var clamped = Vector3F.Clamp(center, -size, size);
        var outward = center - clamped;
        var distance = outward.Length();
        Vector3F localNormal;
        Vector3F surface;
        float separation;

        if (distance > CoincidentDistance)
        {
            localNormal = -outward / distance;
            surface = clamped;
            separation = distance - sphere.Radius;
        }
        else
        {
            var depths = size - Vector3F.Abs(center);
            var axis = depths.X <= depths.Y && depths.X <= depths.Z ? 0 : depths.Y <= depths.Z ? 1 : 2;
            var exit = Vector3F.Zero;
            surface = center;
            float depth;
            if (axis == 0)
            {
                exit.X = center.X >= 0 ? 1 : -1;
                surface.X = exit.X * size.X;
                depth = depths.X;
            }
            else if (axis == 1)
            {
                exit.Y = center.Y >= 0 ? 1 : -1;
                surface.Y = exit.Y * size.Y;
                depth = depths.Y;
            }
            else
            {
                exit.Z = center.Z >= 0 ? 1 : -1;
                surface.Z = exit.Z * size.Z;
                depth = depths.Z;
            }

            localNormal = -exit;
            separation = -depth - sphere.Radius;
        }

        if (separation > speculativeDistance)
            return false;

        var normal = boxPose.TransformDirection(localNormal);
        var onSphere = spherePose.Position + normal * sphere.Radius;
        var onBox = boxPose.TransformPoint(surface);
        manifold.Normal = normal;
        manifold.Add((onSphere + onBox) * 0.5f, separation);
        return true;
    }
}
