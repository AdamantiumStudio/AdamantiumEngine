using System.Collections.Generic;

namespace Adamantium.Graphics.Core.Models;

/// <summary>The edits of one part of a model.</summary>
public sealed class PartEdits
{
    /// <summary>The collider the part's mesh gets in place of the box its import gives; null to keep that box.</summary>
    public ColliderKind? Collider { get; set; }

    /// <summary>The sizes and placement of the part's collider; null for one fitted to the mesh, unscaled.</summary>
    public ColliderShapeEdits ColliderShape { get; set; }

    /// <summary>Whether the part's collider is a trigger, its layer and material; null for the defaults.</summary>
    public ColliderTraits ColliderTraits { get; set; }

    /// <summary>How the physics moves the part in a game; null when the part is no body of its own.</summary>
    public BodyEdits Body { get; set; }

    /// <summary>The part's joints, each of its own kind, holding its body to another part's or to the world; null or
    /// empty for none.</summary>
    public List<JointEdits> Joints { get; set; }
}
