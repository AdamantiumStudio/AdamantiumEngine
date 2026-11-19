namespace Adamantium.Engine.Physics;

/// <summary>Whether an entity's body - its own, or the one above it - is one the physics holds.</summary>
internal enum BodyFound
{
    /// <summary>No rigid body on the entity or above it.</summary>
    None,

    /// <summary>A rigid body the physics has not made, or refused.</summary>
    Waiting,

    /// <summary>A body the physics holds.</summary>
    Made
}
