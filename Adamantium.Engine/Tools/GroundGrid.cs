using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Engine.EntityServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// The ground of the editor's scene: the plane y = <see cref="PlaneY"/> ruled into cells, as editors show it. Cells of 1, 10, 100... times
/// <see cref="CellSize"/> are shown by how big they are on screen, the smaller ones fading as the eye moves away; the X
/// axis runs red along it and the Z axis blue. The grid fades out towards its edge, and what stands or lies on it hides
/// it. The axes are drawn as lines too, seen from any side, and so are the grid's lines while the plane is seen edge on
/// - the eye in it, or a flat view along it - where its pixels have nothing to rule.
/// </summary>
public class GroundGrid : EditorProcessor
{
    private const float ReachPerHeight = 60;
    private const float LeastReachInCells = 200;
    private const float FlatReachPerShown = 3;
    private const float DefaultCellSize = 1;
    private const float DefaultShownCellPoints = 10;
    private const int EdgeLines = 50;
    private readonly Entity edges = new(null, "Ground lines");
    private readonly Entity lines;
    private readonly Entity axisX;
    private readonly Entity axisZ;

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

    public GroundGrid()
    {
        lines = LineEntity("Lines", UnitGrid());
        axisX = LineEntity("X axis", Segment(Vector3.UnitX));
        axisZ = LineEntity("Z axis", Segment(Vector3.UnitZ));
    }

    public override void Update(AppTime appTime)
    {
        var cameras = Tools.Observatory.CurrentCameras;
        for (int i = 0; i < cameras.Count; i++)
        {
            PlaceLines(cameras[i]);
        }
    }

    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        overlay.DrawGrid(this);
        overlay.DrawFlatInScene(edges);
    }

    private void PlaceLines(CameraBase camera)
    {
        var layout = LayoutFor(camera);
        var eye = camera.WorldPosition;
        var below = (float)((float.IsFinite(PlaneY) ? PlaneY : 0) - eye.Y);
        var reach = layout.Reach;
        Place(axisX, camera, Math.Abs(eye.Z) < reach,
            Matrix4x4F.Scaling(reach) * Matrix4x4F.Translation(0, below, (float)-eye.Z), AxisXColor, 1);
        Place(axisZ, camera, Math.Abs(eye.X) < reach,
            Matrix4x4F.Scaling(reach) * Matrix4x4F.Translation((float)-eye.X, below, 0), AxisZColor, 1);

        var cell = layout.Cell;
        while (cell * EdgeLines < reach)
        {
            cell *= 10;
        }

        var edgeOn = EdgeOn(camera, below, cell);
        var phaseX = (float)Modulo(eye.X, cell);
        var phaseZ = (float)Modulo(eye.Z, cell);
        Place(lines, camera, edgeOn > 0.01f,
            Matrix4x4F.Scaling(cell, 1, cell) * Matrix4x4F.Translation(-phaseX, below, -phaseZ), MajorColor, edgeOn);
    }

    private static float EdgeOn(CameraBase camera, float below, float cell)
    {
        var forward = (Vector3F)camera.Forward;
        var across = new Vector3F(forward.X, 0, forward.Z);
        if (across.Length() < 0.1f)
        {
            return 0;
        }

        across = Vector3F.Normalize(across);
        var ahead = Math.Max((float)camera.FocusDistance, cell);
        var near = across * ahead + new Vector3F(0, below, 0);
        var far = near + across * cell;
        if (Vector3F.Dot(near, forward) <= camera.ZNear && camera.ProjectionMatrix.M44 < 0.5f)
        {
            return 0;
        }

        var covered = Vector2F.Distance(ScreenSpace.ToPixel(near, camera), ScreenSpace.ToPixel(far, camera));
        var t = Math.Clamp((covered - 1) / 5, 0, 1);
        return 1 - t * t * (3 - 2 * t);
    }

    private static void Place(Entity line, CameraBase camera, bool shown, Matrix4x4F world, Vector4F color, float strength)
    {
        var metadata = line.Transform.GetMetadata(camera);
        metadata.Enabled = shown;
        if (!shown)
        {
            return;
        }

        metadata.WorldMatrixF = world;
        var material = line.GetComponent<Material>();
        material.MeshColor = new Vector3F(color.X, color.Y, color.Z);
        material.Transparency = color.W * strength;
    }

    private Entity LineEntity(string name, Mesh mesh)
    {
        var entity = new Entity(edges, name);
        entity.AddComponent(new MeshData { Mesh = mesh });
        entity.AddComponent(new Material { HighlightColor = Vector4F.One, Transparency = 1 });
        return entity;
    }

    private static Mesh Segment(Vector3 along)
    {
        var mesh = new Mesh(PrimitiveType.LineList);
        mesh.SetPoints(new List<Vector3> { -along, along });
        mesh.SetIndices(new List<int> { 0, 1 });
        return mesh;
    }

    private static Mesh UnitGrid()
    {
        var points = new List<Vector3>();
        var indices = new List<int>();
        for (int i = -EdgeLines; i <= EdgeLines; i++)
        {
            indices.Add(points.Count);
            points.Add(new Vector3(i, 0, -EdgeLines));
            indices.Add(points.Count);
            points.Add(new Vector3(i, 0, EdgeLines));
            indices.Add(points.Count);
            points.Add(new Vector3(-EdgeLines, 0, i));
            indices.Add(points.Count);
            points.Add(new Vector3(EdgeLines, 0, i));
        }

        var mesh = new Mesh(PrimitiveType.LineList);
        mesh.SetPoints(points);
        mesh.SetIndices(indices);
        return mesh;
    }

    /// <summary>Where the grid lies for <paramref name="camera"/>: it reaches sixty times the eye's height above the
    /// grid, never less than two hundred cells - in a flat view far enough past all it shows that it fades only off
    /// screen - and never past nine tenths of the far plane.</summary>
    public GroundGridLayout LayoutFor(CameraBase camera)
    {
        var eye = camera.WorldPosition;
        var cell = Cell;
        var below = (float)((float.IsFinite(PlaneY) ? PlaneY : 0) - eye.Y);
        var reach = Math.Max(Math.Abs(below) * ReachPerHeight, cell * LeastReachInCells);
        var projection = camera.ProjectionMatrix;
        if (projection.M44 > 0.5f)
        {
            var forward = camera.Forward;
            var drop = Math.Max(Math.Abs((float)forward.Y), 0.05f);
            var shown = Math.Max(1 / projection.M11, 1 / projection.M22 / drop);
            var off = (float)(Math.Sqrt(forward.X * forward.X + forward.Z * forward.Z) * camera.FocusDistance);
            reach = Math.Max(reach, FlatReachPerShown * (shown + off));
        }

        reach = Math.Min(reach, camera.ZFar * 0.9f);
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
