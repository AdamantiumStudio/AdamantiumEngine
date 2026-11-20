namespace Adamantium.DarkMatter.Simulation;

/// <summary>What a command of a <see cref="PhysicsSimulation"/> asks the core to do.</summary>
internal enum SimulationCommandKind
{
    SetGravity,
    AddBody,
    RemoveBody,
    ApplyImpulse,
    ApplyImpulseAtPoint,
    ApplyAngularImpulse,
    SetLinearVelocity,
    SetAngularVelocity,
    SetPose,
    MoveKinematic,
    WakeUp,
    SetKind,
    SetShape,
    SetMaterial,
    AddJoint,
    RemoveJoint,
    SetLimits,
    SetMotor
}
