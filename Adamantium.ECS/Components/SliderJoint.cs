using Adamantium.DarkMatter;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>A slider: the bodies keep their turn and slide along its axis alone - a drawer, a piston - within limits,
/// and driven by a motor if it has one.</summary>
[ViewModel]
[Addable(ComponentGroups.Joints, Icon = "SliderJointIcon")]
public partial class SliderJoint : Joint
{
    public SliderJoint()
    {
        Axis = Vector3F.UnitX;
        LowerLimit = -1;
        UpperLimit = 1;
    }

    public override JointKind Kind => JointKind.Slider;

    /// <summary>The axis it slides along, in its entity's own space.</summary>
    [Bindable]
    public partial Vector3F Axis { get; set; }

    /// <summary>Whether the slide is held between <see cref="LowerLimit"/> and <see cref="UpperLimit"/>.</summary>
    [Bindable]
    public partial bool UsesLimits { get; set; }

    /// <summary>How far it may slide back from where it stood when the game started, in meters. Set above
    /// <see cref="UpperLimit"/>, it takes that along: limits the wrong way round are no slider the physics can make.
    /// </summary>
    [Bindable]
    public partial float LowerLimit { get; set; }

    /// <summary>How far it may slide on, in meters. Set below <see cref="LowerLimit"/>, it takes that along.</summary>
    [Bindable]
    public partial float UpperLimit { get; set; }

    /// <summary>Whether a motor drives it at <see cref="MotorSpeed"/>.</summary>
    [Bindable]
    public partial bool UsesMotor { get; set; }

    /// <summary>The speed the motor drives it at, in meters per second.</summary>
    [Bindable]
    public partial float MotorSpeed { get; set; }

    /// <summary>The most force the motor gives, in newtons.</summary>
    [Bindable]
    public partial float MaxMotorForce { get; set; }

    partial void OnLowerLimitChanged(float value)
    {
        if (value > UpperLimit)
            UpperLimit = value;
    }

    partial void OnUpperLimitChanged(float value)
    {
        if (value < LowerLimit)
            LowerLimit = value;
    }
}
