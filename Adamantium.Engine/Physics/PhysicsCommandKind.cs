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
    ApplyImpulse,
    AddBody,

    /// <summary>A body made anew in place of the key's one, moving as that one did.</summary>
    ReplaceBody,

    /// <summary>The key's body changed where it stands: its kind, settings and shape, and put where its entity was
    /// moved by hand.</summary>
    ChangeBody,
    RemoveBody
}
