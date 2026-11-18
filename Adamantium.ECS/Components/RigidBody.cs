using Adamantium.DarkMatter;
using Adamantium.MVVM;

namespace Adamantium.ECS.Components;

/// <summary>Makes its entity a body the physics moves while a game plays, colliding with the shape of the entity's
/// collider: a box, a sphere or a capsule.</summary>
[ViewModel]
public partial class RigidBody : Component
{
    public RigidBody()
    {
        Kind = BodyKind.Dynamic;
        Density = 1000f;
        Friction = 0.6f;
        GravityScale = 1f;
        CanSleep = true;
    }

    /// <summary>How the body moves: static, kinematic or dynamic.</summary>
    [Bindable]
    public partial BodyKind Kind { get; set; }

    /// <summary>The mass in kilograms; zero to have it follow from <see cref="Density"/>.</summary>
    [Bindable]
    public partial float Mass { get; set; }

    /// <summary>The density the mass follows from, in kilograms per cubic meter; water by default.</summary>
    [Bindable]
    public partial float Density { get; set; }

    /// <summary>The friction against other bodies.</summary>
    [Bindable]
    public partial float Friction { get; set; }

    /// <summary>How much of its speed the body keeps bouncing off: 0 not at all, 1 all of it.</summary>
    [Bindable]
    public partial float Restitution { get; set; }

    /// <summary>How fast the body's velocity fades by itself, per second.</summary>
    [Bindable]
    public partial float LinearDamping { get; set; }

    /// <summary>How fast the body's spin fades by itself, per second.</summary>
    [Bindable]
    public partial float AngularDamping { get; set; }

    /// <summary>How much gravity pulls the body: 1 as the world's, 0 not at all.</summary>
    [Bindable]
    public partial float GravityScale { get; set; }

    /// <summary>Whether the body may fall asleep when it comes to rest.</summary>
    [Bindable]
    public partial bool CanSleep { get; set; }
}
