namespace Adamantium.DarkMatter.Shapes;

/// <summary>Which shape a <see cref="CollisionShape"/> is; the order is the order pairs are tested in.</summary>
public enum CollisionShapeKind
{
    Sphere,
    Capsule,
    Box
}
