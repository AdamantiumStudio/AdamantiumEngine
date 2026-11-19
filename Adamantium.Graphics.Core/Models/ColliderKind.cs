namespace Adamantium.Graphics.Core.Models;

/// <summary>The collider a part of a model gets, fitted to its mesh. Kept as numbers in the asset file: new kinds come
/// last.</summary>
public enum ColliderKind
{
    /// <summary>No collider: the physics does not meet it; picking in the viewport goes by its triangles.</summary>
    None,
    Box,
    Sphere,
    Capsule,
    Cylinder,

    /// <summary>The convex hull of the part's mesh.</summary>
    ConvexHull,

    /// <summary>The part's mesh itself, triangle by triangle: only for a body that does not move by forces.</summary>
    Mesh
}
