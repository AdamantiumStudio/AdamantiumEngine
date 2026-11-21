using Adamantium.DarkMatter;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Physics;

/// <summary>A joint as the game asks the physics for it: its bodies, and its points and axis on each body from the
/// body's origin, scaled as the body was made - so it is right wherever the bodies stand once the simulation takes it.
/// Alike requests ask for the same joint; a value, so asking costs nothing.</summary>
internal readonly record struct JointRequest
{
    public JointKind Kind { get; init; }

    /// <summary>The body the joint's component is on.</summary>
    public BodyId Owner { get; init; }

    /// <summary>The body it is held to; none for the world.</summary>
    public BodyId Connected { get; init; }

    /// <summary>The anchor on the owner's body, from its origin in its own axes.</summary>
    public Vector3F Anchor { get; init; }

    /// <summary>The axis on the owner's body, in its own axes, of length one.</summary>
    public Vector3F Axis { get; init; }

    /// <summary>A distance joint's other end: on the connected body from its origin, or in the world for none.
    /// </summary>
    public Vector3<double> ConnectedAnchor { get; init; }

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
