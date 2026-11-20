using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Finds where two shapes touch or come within the speculative distance of each other.</summary>
internal static class NarrowPhase
{
    /// <summary>Fills <paramref name="manifold"/> with the contact of the two shapes, its normal from the first to the
    /// second; false when they stand farther apart than <paramref name="speculativeDistance"/>.</summary>
    public static bool Collide(CollisionShape first, in LocalPose firstPose, CollisionShape second, in LocalPose secondPose,
        float speculativeDistance, ManifoldBuffer manifold)
    {
        manifold.Clear();
        if (TakesTheGeneralPath(first.Kind) || TakesTheGeneralPath(second.Kind))
            return ConvexCollisions.Collide(first, firstPose, second, secondPose, speculativeDistance, manifold);

        if (first.Kind > second.Kind)
        {
            var found = CollideOrdered(second, secondPose, first, firstPose, speculativeDistance, manifold);
            manifold.Normal = -manifold.Normal;
            return found;
        }

        return CollideOrdered(first, firstPose, second, secondPose, speculativeDistance, manifold);
    }

    // Shapes with no contact code of their own: any pair with one of them is met by GJK, EPA and clipped faces.
    private static bool TakesTheGeneralPath(CollisionShapeKind kind) =>
        kind is CollisionShapeKind.ConvexHull or CollisionShapeKind.Cylinder;

    private static bool CollideOrdered(CollisionShape first, in LocalPose firstPose, CollisionShape second,
        in LocalPose secondPose, float speculativeDistance, ManifoldBuffer manifold)
    {
        switch (first)
        {
            case SphereCollisionShape sphere when second is SphereCollisionShape otherSphere:
                return SphereCollisions.CollideSpheres(sphere, firstPose, otherSphere, secondPose, speculativeDistance,
                    manifold);
            case SphereCollisionShape sphere when second is CapsuleCollisionShape capsule:
                return SphereCollisions.CollideSphereCapsule(sphere, firstPose, capsule, secondPose, speculativeDistance,
                    manifold);
            case SphereCollisionShape sphere when second is BoxCollisionShape box:
                return SphereCollisions.CollideSphereBox(sphere, firstPose, box, secondPose, speculativeDistance,
                    manifold);
            case CapsuleCollisionShape capsule when second is CapsuleCollisionShape otherCapsule:
                return CapsuleCollisions.CollideCapsules(capsule, firstPose, otherCapsule, secondPose,
                    speculativeDistance, manifold);
            case CapsuleCollisionShape capsule when second is BoxCollisionShape box:
                return CapsuleCollisions.CollideCapsuleBox(capsule, firstPose, box, secondPose, speculativeDistance,
                    manifold);
            case BoxCollisionShape box when second is BoxCollisionShape otherBox:
                return BoxCollisions.CollideBoxes(box, firstPose, otherBox, secondPose, speculativeDistance, manifold);
            default:
                return false;
        }
    }
}
