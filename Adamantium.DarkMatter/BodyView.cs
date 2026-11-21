using Adamantium.DarkMatter.Simulation;

namespace Adamantium.DarkMatter;

/// <summary>A body as a search of <see cref="PhysicsSimulation.Find"/> sees it: what the last step read left of it,
/// read in place, nothing copied. Good while the search walks.</summary>
public readonly struct BodyView
{
    private readonly SimulatedBody _body;

    internal BodyView(BodyId id, SimulatedBody body)
    {
        Id = id;
        _body = body;
    }

    public BodyId Id { get; }
    public BodyKind Kind => _body.Kind;
    public float Mass => _body.Mass;
    public bool IsSleeping => _body.IsSleeping;
    public Layer Layer => _body.Layer;

    /// <summary>Where its origin stands, and how it is turned.</summary>
    public Pose Pose => _body.Pose;
    public WorldPosition CenterOfMass => _body.CenterOfMass;
    public Vector3F LinearVelocity => _body.LinearVelocity;
    public Vector3F AngularVelocity => _body.AngularVelocity;

    /// <summary>How fast its center of mass moves, in meters per second.</summary>
    public float Speed => _body.LinearVelocity.Length();

    /// <summary>The caller's own given with it.</summary>
    public object Data => _body.Data;

    /// <summary>The kind it will have once the actions asked are done, which the actions on found bodies go by.
    /// </summary>
    internal BodyKind KindAsked => _body.KindAsked;

    // Whether the walk gives the body for the first time, marked given if so.
    internal bool TakeIntoWalk(long walk)
    {
        if (_body.WalkedIn == walk)
            return false;

        _body.WalkedIn = walk;
        return true;
    }
}
