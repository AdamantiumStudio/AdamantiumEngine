using Adamantium.DarkMatter;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>Welds its entity's body to another's, or to the world: they move as one.</summary>
[ViewModel]
[Addable(ComponentGroups.Joints, Icon = "FixedJointIcon")]
public partial class FixedJoint : Joint
{
    public override JointKind Kind => JointKind.Fixed;
}
