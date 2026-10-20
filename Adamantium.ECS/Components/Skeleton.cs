using System.Collections.Generic;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Components;

/// <summary>
/// The joints a model's skinned meshes bend with - entities standing for them, posed by its animation or by hand - and
/// the inverse bind matrix of each, in the order a vertex's joint indices count them. One for all the parts of a mesh
/// that share them: each part's <see cref="Skin"/> holds the same one.
/// </summary>
public sealed class Skeleton
{
    /// <summary>The joints, as a vertex's joint indices count them.</summary>
    public List<Entity> Joints { get; } = [];

    /// <summary>For each joint, what takes the mesh from its own space to the joint's at the pose it was bound in.</summary>
    public List<Matrix4x4F> InverseBindMatrices { get; } = [];
}
