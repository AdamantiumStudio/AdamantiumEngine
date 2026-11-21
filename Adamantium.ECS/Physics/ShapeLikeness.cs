using System;
using Adamantium.DarkMatter;
using Adamantium.DarkMatter.Shapes;

namespace Adamantium.ECS.Physics;

/// <summary>Whether two shapes made for a body are the same shape: of one kind and size, or one hull shape shared. Sizes
/// and places read back from a turned entity's matrix differ in their last digits, so they count as alike that close.
/// </summary>
internal static class ShapeLikeness
{
    private const float Closeness = 1e-5f;

    /// <summary>Whether two bodies' parts are alike, each made of the same collider.</summary>
    public static bool AreAlike(BodyPart[] first, BodyPart[] second)
    {
        if (first.Length != second.Length)
            return false;

        for (var index = 0; index < first.Length; index++)
        {
            var firstPose = first[index].Pose;
            var secondPose = second[index].Pose;
            if (!ReferenceEquals(first[index].Data, second[index].Data)
                || !AreClose(firstPose.Position, secondPose.Position) || !AreClose(firstPose.Rotation, secondPose.Rotation)
                || !AreAlike(first[index].Shape, second[index].Shape))
                return false;
        }

        return true;
    }

    // Hull shapes come from the game's cache, its key rounded: the same hull at the same scale is the same shape object.
    private static bool AreAlike(CollisionShape first, CollisionShape second)
    {
        if (ReferenceEquals(first, second))
            return true;

        if (first is BoxCollisionShape box && second is BoxCollisionShape otherBox)
            return AreClose(box.HalfExtents, otherBox.HalfExtents);

        if (first is SphereCollisionShape sphere && second is SphereCollisionShape otherSphere)
            return AreClose(sphere.Radius, otherSphere.Radius);

        if (first is CapsuleCollisionShape capsule && second is CapsuleCollisionShape otherCapsule)
            return AreClose(capsule.HalfLength, otherCapsule.HalfLength) && AreClose(capsule.Radius, otherCapsule.Radius);

        return first is CylinderCollisionShape cylinder && second is CylinderCollisionShape otherCylinder
               && AreClose(cylinder.HalfHeight, otherCylinder.HalfHeight)
               && AreClose(cylinder.Radius, otherCylinder.Radius)
               && AreClose(cylinder.ConvexRadius, otherCylinder.ConvexRadius);
    }

    private static bool AreClose(float first, float second) =>
        MathF.Abs(first - second) <= Closeness * MathF.Max(1, MathF.Max(MathF.Abs(first), MathF.Abs(second)));

    private static bool AreClose(Vector3F first, Vector3F second) =>
        AreClose(first.X, second.X) && AreClose(first.Y, second.Y) && AreClose(first.Z, second.Z);

    // A turn and its negation are one turn.
    private static bool AreClose(QuaternionF first, QuaternionF second) =>
        MathF.Abs(MathF.Abs(QuaternionF.Dot(first, second)) - 1) <= Closeness;
}
