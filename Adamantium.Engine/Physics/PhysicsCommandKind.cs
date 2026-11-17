namespace Adamantium.Engine.Physics;

/// <summary>What a game asks of its physics.</summary>
internal enum PhysicsCommandKind
{
    SetGravity,
    SetLinearVelocity,
    SetAngularVelocity,

    /// <summary>A frame's forces, pushing for the frame's length; less of both when the physics dropped time.</summary>
    PushForFrame,
    ApplyCentralImpulse,
    ApplyImpulse
}
