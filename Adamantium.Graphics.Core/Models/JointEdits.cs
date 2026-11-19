using Adamantium.DarkMatter;
using Adamantium.Mathematics;

namespace Adamantium.Graphics.Core.Models;

/// <summary>A part's joint, as a model keeps it: what its joint component is given when the model is built. The fields
/// a kind has no use for are left as they are.</summary>
public sealed record JointEdits
{
    public JointKind Kind { get; set; } = JointKind.Ball;

    /// <summary>The path of the part of the same model whose body the joint holds to; null for the world.</summary>
    public string ConnectedPart { get; set; }

    /// <summary>The joint's point, in its part's own space.</summary>
    public Vector3F Anchor { get; set; }

    /// <summary>A hinge's or a slider's axis, in its part's own space.</summary>
    public Vector3F Axis { get; set; } = Vector3F.UnitY;

    /// <summary>A distance joint's other end, in the connected part's own space, or in the world for none.</summary>
    public Vector3F ConnectedAnchor { get; set; }

    public bool CollideConnected { get; set; }

    public bool UsesLimits { get; set; }

    /// <summary>A hinge's in degrees, a slider's offset or a distance in meters.</summary>
    public float LowerLimit { get; set; }

    public float UpperLimit { get; set; }

    public bool UsesMotor { get; set; }

    /// <summary>In degrees per second for a hinge, meters per second for a slider.</summary>
    public float MotorSpeed { get; set; }

    /// <summary>Newton meters for a hinge, newtons for a slider.</summary>
    public float MaxMotorForce { get; set; }

    public float SpringHertz { get; set; }

    public float SpringDampingRatio { get; set; } = 1;
}
