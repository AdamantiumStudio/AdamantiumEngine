using Adamantium.DarkMatter;
using Adamantium.Mathematics;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>A rod, a rope or a spring from its anchor to a point of the other body: their distance kept as it was when
/// the game started, held between limits, or pulled toward it by a spring.</summary>
[ViewModel]
public partial class DistanceJoint : Joint
{
    public DistanceJoint()
    {
        MaxDistance = 1;
        SpringDampingRatio = 1;
    }

    public override JointKind Kind => JointKind.Distance;

    /// <summary>The other end, in the connected entity's own space, or in the world for none.</summary>
    [Bindable]
    public partial Vector3<double> ConnectedAnchor { get; set; }

    /// <summary>Whether the distance is held between <see cref="MinDistance"/> and <see cref="MaxDistance"/>, as a
    /// rope, and not kept as it was.</summary>
    [Bindable]
    public partial bool UsesLimits { get; set; }

    /// <summary>The shortest the distance may be, in meters.</summary>
    [Bindable]
    public partial float MinDistance { get; set; }

    /// <summary>The longest the distance may be, in meters.</summary>
    [Bindable]
    public partial float MaxDistance { get; set; }

    /// <summary>A spring pulling toward the distance it had when the game started, in hertz; zero keeps it rigid.
    /// </summary>
    [Bindable]
    public partial float SpringHertz { get; set; }

    /// <summary>How the spring's swing dies: 1 not at all past its rest.</summary>
    [Bindable]
    public partial float SpringDampingRatio { get; set; }
}
