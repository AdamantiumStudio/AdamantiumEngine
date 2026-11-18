namespace Adamantium.Engine.Physics;

/// <summary>What a game asks of its physics.</summary>
internal enum PhysicsCommandKind
{
    SetGravity,
    SetLinearVelocity,
    SetAngularVelocity,

    /// <summary>The force a body feels at every step from now on; zero ends it.</summary>
    SetForce,
    ApplyCentralImpulse,
    ApplyImpulse
}
