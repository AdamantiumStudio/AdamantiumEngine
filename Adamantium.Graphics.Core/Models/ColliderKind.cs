namespace Adamantium.Graphics.Core.Models;

/// <summary>The collider a part of a model gets, fitted to its mesh. Kept as numbers in the asset file: new kinds come
/// last.</summary>
public enum ColliderKind
{
    /// <summary>No collider: the part is not met by the physics or picked by its collider.</summary>
    None,
    Box,
    Sphere,
    Capsule,
    Cylinder,

    /// <summary>The convex hull of the part's mesh.</summary>
    ConvexHull
}
