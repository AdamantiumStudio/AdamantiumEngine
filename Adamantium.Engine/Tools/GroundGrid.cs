using System;
using Adamantium.ECS.Components;
using Adamantium.Engine.EntityServices;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// The ground of the editor's scene: the plane y = <see cref="PlaneY"/> ruled into cells, as editors show it. Cells of 1, 10, 100... times
/// <see cref="CellSize"/> are shown by how big they are on screen, the smaller ones fading as the eye moves away; the X
/// axis runs red along it and the Z axis blue. The grid fades out towards its edge, and what stands or lies on it hides
/// it.
/// </summary>
public class GroundGrid : EditorProcessor
{
    private const float ReachPerHeight = 60;
    private const float LeastReachInCells = 200;
    private const float DefaultCellSize = 1;
    private const float DefaultShownCellPoints = 10;

    /// <summary>How many levels of cells, 1, 10, 100... times <see cref="CellSize"/>, the grid has phases for; bigger
    /// cells are not drawn.</summary>
    public const int Levels = 8;

    /// <summary>The y the grid lies at, 0 by default, or if it is no number. Up is -Y, so -2 lays it two units above the
    /// world's ground.</summary>
    public float PlaneY { get; set; }

    /// <summary>The smallest cell, in world units: from a millionth to a million, or a unit if it is none.</summary>
    public float CellSize { get; set; } = DefaultCellSize;

    /// <summary>How wide a line is drawn, in points.</summary>
    public float LineWidth { get; set; } = 1;

    /// <summary>How wide a level's cells are on screen, in points, when its lines are at <see cref="MinorColor"/>'s
    /// strength: they fade in from nothing at a tenth of that and reach <see cref="MajorColor"/>'s at ten times it.</summary>
    public float ShownCellPoints { get; set; } = DefaultShownCellPoints;

    /// <summary>The lines of the smallest cells shown; the alpha is their strength.</summary>
    public Vector4F MinorColor { get; set; } = new(0.55f, 0.55f, 0.55f, 0.2f);

    /// <summary>The lines of the cells ten times bigger and up.</summary>
    public Vector4F MajorColor { get; set; } = new(0.7f, 0.7f, 0.7f, 0.5f);

    /// <summary>The X axis, the line z = 0.</summary>
    public Vector4F AxisXColor { get; set; } = new(0.86f, 0.27f, 0.27f, 0.9f);

    /// <summary>The Z axis, the line x = 0.</summary>
    public Vector4F AxisZColor { get; set; } = new(0.27f, 0.47f, 0.95f, 0.9f);

    /// <summary><see cref="ShownCellPoints"/> if it is a size on screen, or its default.</summary>
    public float ShownCellPointsOrDefault => float.IsFinite(ShownCellPoints) && ShownCellPoints >= 1
        ? ShownCellPoints
        : DefaultShownCellPoints;

    private float Cell => float.IsFinite(CellSize) && CellSize > 0 ? Math.Clamp(CellSize, 1e-6f, 1e6f) : DefaultCellSize;

    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        overlay.DrawGrid(this);
    }

    /// <summary>Where the grid lies for <paramref name="camera"/>: it reaches sixty times the eye's height above the
    /// grid, never less than two hundred cells, and never past nine tenths of the far plane.</summary>
    public GroundGridLayout LayoutFor(CameraBase camera)
    {
        var eye = camera.WorldPosition;
        var cell = Cell;
        var below = (float)((float.IsFinite(PlaneY) ? PlaneY : 0) - eye.Y);
        var reach = Math.Min(Math.Max(Math.Abs(below) * ReachPerHeight, cell * LeastReachInCells), camera.ZFar * 0.9f);
        var placement = Matrix4x4F.Scaling(reach) * Matrix4x4F.RotationX(MathF.PI / 2) * Matrix4x4F.Translation(0, below, 0);
        var axes = new Vector2F(Math.Clamp((float)-eye.X, -2 * reach, 2 * reach), Math.Clamp((float)-eye.Z, -2 * reach, 2 * reach));
        return new GroundGridLayout(placement, axes, reach, cell);
    }

    /// <summary>
    /// Writes the eye's place in the ground plane modulo the cells of each level into <paramref name="phases"/>, two
    /// levels to an entry: x and z of the smallest cells, then of those ten times bigger, and so on. What a level's lines
    /// are drawn from stays as small as its cells, so they hold still however far the eye has gone. The cell is taken by
    /// its decimal form: a float tenth is a little more than a tenth, and that little, times the cells the eye has gone,
    /// would move the lines off the world's.
    /// </summary>
    public void PhasesFor(CameraBase camera, Vector4F[] phases)
    {
        var eye = camera.WorldPosition;
        var cell = (double)(decimal)Cell;
        for (int level = 0; level < Levels; level += 2)
        {
            var next = cell * 10;
            phases[level / 2] = new Vector4F((float)Modulo(eye.X, cell), (float)Modulo(eye.Z, cell),
                (float)Modulo(eye.X, next), (float)Modulo(eye.Z, next));
            cell = next * 10;
        }
    }

    private static double Modulo(double value, double period)
    {
        var rest = value % period;
        return rest < 0 ? rest + period : rest;
    }
}
