using Adamantium.Mathematics;

namespace Adamantium.ECS.Components;

/// <summary>A hull collider's corners where one camera sees its entity, and the matrix that put them there.</summary>
internal sealed class PlacedCorners
{
    public Matrix4x4F World { get; set; }

    public Vector3F[] Corners { get; set; } = [];
}
