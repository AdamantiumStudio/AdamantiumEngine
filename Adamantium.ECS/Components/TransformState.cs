using Adamantium.Mathematics;

namespace Adamantium.ECS.Components;

/// <summary>Everything a <see cref="Transform"/> places its entity by, as it stood at one moment: given back with
/// <see cref="Transform.State"/>, it puts the entity exactly where it was, its pivot with it.</summary>
/// <param name="Position">The entity's position in its parent's space.</param>
/// <param name="Rotation">The entity's rotation in its parent's space.</param>
/// <param name="ScaleFactor">The scale applied over the entity's base scale.</param>
/// <param name="PivotOffset">Where the pivot stands from the position, as the transform keeps it.</param>
/// <param name="PivotRotation">The rotation of the pivot's own axes.</param>
public readonly record struct TransformState(
    Vector3<double> Position,
    QuaternionF Rotation,
    Vector3F ScaleFactor,
    Vector3<double> PivotOffset,
    QuaternionF PivotRotation);
