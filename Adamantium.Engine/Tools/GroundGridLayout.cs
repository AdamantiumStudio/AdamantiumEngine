using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Where the ground grid lies for one camera, in the camera-relative space the scene is drawn in.
/// </summary>
/// <param name="Placement">Lays the unit square of the grid flat on the ground round its center, as far as it reaches.</param>
/// <param name="Axes">Where the X axis (z = 0) and the Z axis (x = 0) cross the plane relative to the eye, along x and
/// z, held to the grid's reach.</param>
/// <param name="Reach">How far from its center the grid reaches before it has faded away.</param>
/// <param name="Cell">The smallest cell, in world units: <see cref="GroundGrid.CellSize"/> if it is one.</param>
public readonly record struct GroundGridLayout(Matrix4x4F Placement, Vector2F Axes, float Reach, float Cell)
{
    /// <summary>The grid's center relative to the eye, along x and z: none in a perspective; in a flat view, past the
    /// ground at the screen's near edge (<see cref="GroundGrid.CenterFor"/>).</summary>
    public Vector2F Center { get; init; }
}
