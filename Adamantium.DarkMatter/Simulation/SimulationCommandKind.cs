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
    SetForce,
    SetTorque,
    SetPose,
    MoveKinematic,
    WakeUp,
    SetKind,
    SetShape,
    SetMaterial,
    SetLayer,
    SetLayersCollide,
    SetMass,
    SetDamping,
    SetGravityScale,
    SetCanSleep,
    AddJoint,
    RemoveJoint,
    SetLimits,
    SetMotor,
    AddStepHook,
    RemoveStepHook
}
