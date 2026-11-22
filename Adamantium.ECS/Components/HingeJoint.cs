using Adamantium.DarkMatter;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>A hinge at its anchor: the bodies turn about its axis alone - a door, a wheel - within limits, and driven by
/// a motor if it has one.</summary>
[ViewModel]
[Addable(ComponentGroups.Joints, Icon = "HingeJointIcon")]
public partial class HingeJoint : Joint
{
    public HingeJoint()
    {
        Axis = Vector3F.UnitY;
        LowerAngle = -90;
        UpperAngle = 90;
    }

    public override JointKind Kind => JointKind.Hinge;

    /// <summary>The axis it turns about, in its entity's own space.</summary>
    [Bindable]
    public partial Vector3F Axis { get; set; }

    /// <summary>Whether the turn is held between <see cref="LowerAngle"/> and <see cref="UpperAngle"/>.</summary>
    [Bindable]
    public partial bool UsesLimits { get; set; }

    /// <summary>How far it may turn back from where it stood when the game started, in degrees, to -180.</summary>
    [Bindable]
    public partial float LowerAngle { get; set; }

    /// <summary>How far it may turn on, in degrees, to 180.</summary>
    [Bindable]
    public partial float UpperAngle { get; set; }

    /// <summary>Whether a motor drives it at <see cref="MotorSpeed"/>.</summary>
    [Bindable]
    public partial bool UsesMotor { get; set; }

    /// <summary>The speed the motor drives it at, in degrees per second.</summary>
    [Bindable]
    public partial float MotorSpeed { get; set; }

    /// <summary>The most torque the motor gives, in newton meters.</summary>
    [Bindable]
    public partial float MaxMotorTorque { get; set; }
}
