namespace Adamantium.ECS.Components;

/// <summary>The groups the editor lists the engine's addable components in (<see cref="AddableAttribute"/>), by the
/// name the editor knows them by.</summary>
public static class ComponentGroups
{
    /// <summary>A rigid body and the colliders.</summary>
    public const string Physics = "Physics";

    /// <summary>What holds one body to another.</summary>
    public const string Joints = "Joints";
}
