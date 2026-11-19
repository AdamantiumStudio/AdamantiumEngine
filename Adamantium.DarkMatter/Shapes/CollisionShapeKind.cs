namespace Adamantium.DarkMatter.Shapes;

/// <summary>Which shape a <see cref="CollisionShape"/> is; the order is the order pairs are tested in.</summary>
public enum CollisionShapeKind
{
    Sphere,
    Capsule,
    Box,
    ConvexHull,
    Cylinder,

    /// <summary>One triangle of a surface, as the world meets it with a body.</summary>
    Triangle,

    /// <summary>A surface of triangles: a level, a building, a rock face.</summary>
    TriangleMesh,

    /// <summary>A surface of heights over a grid: ground.</summary>
    HeightField,

    /// <summary>Shapes standing together; the world tests each of them on its own.</summary>
    Compound
}
