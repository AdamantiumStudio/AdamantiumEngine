namespace Adamantium.Graphics.Core.Models;

/// <summary>The edits of one part of a model.</summary>
public sealed class PartEdits
{
    /// <summary>The collider the part's mesh gets in place of the box its import gives; null to keep that box.</summary>
    public ColliderKind? Collider { get; set; }

    /// <summary>The sizes and placement of the part's collider; null for one fitted to the mesh, unscaled.</summary>
    public ColliderShapeEdits ColliderShape { get; set; }

    /// <summary>How the physics moves the part in a game; null when the part is no body of its own.</summary>
    public BodyEdits Body { get; set; }

    /// <summary>The joint holding the part's body to another part's, or to the world; null for none.</summary>
    public JointEdits Joint { get; set; }
}
