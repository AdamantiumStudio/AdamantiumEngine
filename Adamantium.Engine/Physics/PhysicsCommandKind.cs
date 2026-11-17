namespace Adamantium.Engine.Physics;

/// <summary>What a game asks of its physics.</summary>
internal enum PhysicsCommandKind
{
    SetGravity,
    SetLinearVelocity,
    SetAngularVelocity,
    ApplyForce,
    ApplyCentralImpulse,
    ApplyImpulse
}
