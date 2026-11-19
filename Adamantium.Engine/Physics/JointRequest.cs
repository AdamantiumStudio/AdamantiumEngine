using Adamantium.DarkMatter;

namespace Adamantium.Engine.Physics;

/// <summary>A joint as the game asks the physics for it: its bodies by key and generation, and its points and axis on
/// each body from the body's origin, scaled as the body was made - so the physics can add it again wherever the bodies
/// stand once one of them is made anew. Alike requests ask for the same joint; a value, so asking costs nothing.
/// </summary>
internal readonly record struct JointRequest
{
    public JointKind Kind { get; init; }

    /// <summary>The body the joint's component is on, and which body under that key.</summary>
    public int OwnerKey { get; init; }

    public int OwnerGeneration { get; init; }

    /// <summary>The body it is held to, and which; -1 for the world.</summary>
    public int ConnectedKey { get; init; }

    public int ConnectedGeneration { get; init; }

    /// <summary>The anchor on the owner's body, from its origin in its own axes.</summary>
    public Vector3F Anchor { get; init; }

    /// <summary>The axis on the owner's body, in its own axes, of length one.</summary>
    public Vector3F Axis { get; init; }

    /// <summary>A distance joint's other end: on the connected body from its origin, or in the physics' world for none.
    /// </summary>
    public Vector3F ConnectedAnchor { get; init; }

    public bool CollideConnected { get; init; }

    public bool HasLimits { get; init; }

    /// <summary>In radians for a hinge, in meters for a slider and a distance.</summary>
    public float LowerLimit { get; init; }

    public float UpperLimit { get; init; }

    public float MotorSpeed { get; init; }

    public float MaxMotorForce { get; init; }

    public float SpringHertz { get; init; }

    public float SpringDampingRatio { get; init; }
}
