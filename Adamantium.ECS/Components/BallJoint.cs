using Adamantium.DarkMatter;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>A ball in a socket at its anchor: the bodies share the point and turn freely about it.</summary>
[ViewModel]
public partial class BallJoint : Joint
{
    public override JointKind Kind => JointKind.Ball;
}
