using Adamantium.DarkMatter.Shapes;

namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Finds where two shapes touch or come within the speculative distance of each other.</summary>
internal static class Narrowphase
{
    /// <summary>Fills <paramref name="manifold"/> with the contact of the two shapes, its normal from the first to the
    /// second; false when they stand farther apart than <paramref name="speculativeDistance"/>.</summary>
    public static bool Collide(Shape first, in Pose firstPose, Shape second, in Pose secondPose,
        float speculativeDistance, ManifoldBuffer manifold)
    {
        manifold.Clear();
        if (first.Kind > second.Kind)
        {
            var found = CollideOrdered(second, secondPose, first, firstPose, speculativeDistance, manifold);
            manifold.Normal = -manifold.Normal;
            return found;
        }

        return CollideOrdered(first, firstPose, second, secondPose, speculativeDistance, manifold);
    }

    private static bool CollideOrdered(Shape first, in Pose firstPose, Shape second, in Pose secondPose,
        float speculativeDistance, ManifoldBuffer manifold)
    {
        switch (first)
        {
            case SphereShape sphere when second is SphereShape otherSphere:
                return SphereCollisions.CollideSpheres(sphere, firstPose, otherSphere, secondPose, speculativeDistance,
                    manifold);
            case SphereShape sphere when second is CapsuleShape capsule:
                return SphereCollisions.CollideSphereCapsule(sphere, firstPose, capsule, secondPose, speculativeDistance,
                    manifold);
            case SphereShape sphere when second is BoxShape box:
                return SphereCollisions.CollideSphereBox(sphere, firstPose, box, secondPose, speculativeDistance,
                    manifold);
            case CapsuleShape capsule when second is CapsuleShape otherCapsule:
                return CapsuleCollisions.CollideCapsules(capsule, firstPose, otherCapsule, secondPose,
                    speculativeDistance, manifold);
            case CapsuleShape capsule when second is BoxShape box:
                return CapsuleCollisions.CollideCapsuleBox(capsule, firstPose, box, secondPose, speculativeDistance,
                    manifold);
            case BoxShape box when second is BoxShape otherBox:
                return BoxCollisions.CollideBoxes(box, firstPose, otherBox, secondPose, speculativeDistance, manifold);
            default:
                return false;
        }
    }
}
