using System;
using System.Collections.Generic;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// The ground of the editor's scene: the plane y = <see cref="PlaneY"/> ruled into cells, as editors show it. Cells of 1, 10, 100... times
/// <see cref="CellSize"/> are shown by how big they are on screen, the smaller ones fading as the eye moves away; the X
/// axis runs red along it and the Z axis blue. The grid fades out towards its edge, and what stands or lies on it hides
/// it. The axes are drawn as lines too, seen from any side, and so are the grid's lines while a perspective's eye is in
/// the plane, where its pixels have nothing to rule. Under the lines lies a floor of <see cref="FloorColor"/>, which
/// hides what is below the ground; the lines (<see cref="ShowsGrid"/>) and the floor (<see cref="ShowsFloor"/>) are each
/// shown or not.
/// </summary>
public class GroundGrid : EditorProcessor
{
    private const float ReachPerHeight = 60;
    private const float LeastReachInCells = 200;
    private const float FlatReachPerShown = 3;
    private const float WholeShare = 0.4f;
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

    /// <summary>Whether the lines of the cells and the axes are drawn.</summary>
    public bool ShowsGrid { get; set; } = true;

    /// <summary>Whether the floor is drawn under the lines.</summary>
    public bool ShowsFloor { get; set; } = true;

    /// <summary>The floor's color, an even gray by default; its alpha is how much it hides of what is below the ground.
    /// </summary>
    public Vector4F FloorColor { get; set; } = new(0.2f, 0.21f, 0.23f, 1f);

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
        axisX = LineEntity("X axis", Segment(Vector3<double>.UnitX));
        axisZ = LineEntity("Z axis", Segment(Vector3<double>.UnitZ));
    }

    // The lines take their place as they are drawn, from the camera drawing them: placed in Update, they kept the eye of
    // before a camera moved later in the frame, and showed beside the cells the shader drew from the eye of now.
    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        if (ShowsGrid || ShowsFloor)
        {
            overlay.DrawGrid(this);
        }

        if (ShowsGrid)
        {
            PlaceLines(overlay.Camera);
            overlay.DrawFlatBehindScene(edges);
        }
    }

    private void PlaceLines(CameraBase camera)
    {
        var layout = LayoutFor(camera);
        var eye = camera.WorldPosition;
        var offX = layout.Center.X;
        var offZ = layout.Center.Y;
        var center = new Vector3<double>(eye.X + offX, eye.Y, eye.Z + offZ);
        var below = (float)((float.IsFinite(PlaneY) ? PlaneY : 0) - eye.Y);
        var reach = layout.Reach;
        Place(axisX, camera, Math.Abs(center.Z) < reach,
            Matrix4x4.CreateScale<float>(reach) * Matrix4x4.CreateTranslation<float>(offX, below, (float)-eye.Z), AxisXColor, 1);
        Place(axisZ, camera, Math.Abs(center.X) < reach,
            Matrix4x4.CreateScale<float>(reach) * Matrix4x4.CreateTranslation<float>((float)-eye.X, below, offZ), AxisZColor, 1);

        var cell = layout.Cell;
        while (cell * EdgeLines < reach)
        {
            cell *= 10;
        }

        var edgeOn = EdgeOn(camera, below, cell);
        var phaseX = (float)Modulo(center.X, cell);
        var phaseZ = (float)Modulo(center.Z, cell);
        Place(lines, camera, edgeOn > 0.01f,
            Matrix4x4.CreateScale<float>(cell, 1, cell) * Matrix4x4.CreateTranslation<float>(offX - phaseX, below, offZ - phaseZ), MajorColor, edgeOn);
    }

    /// <summary>
    /// Where in the world the grid is centered for <paramref name="camera"/>, on the ground: under the eye in a
    /// perspective, as a perspective's ground runs out from it. In a flat view - which shows the same wherever along its
    /// view the eye stands, often far behind what it looks at - far enough past the ground at the screen's near edge
    /// that the grid shows whole down to that edge and fades only far off, as toward a horizon; looking straight down,
    /// under the point the camera looks at.
    /// </summary>
    public Vector3<double> CenterFor(CameraBase camera)
    {
        var eye = camera.WorldPosition;
        if (camera.ProjectionMatrix.M44 < 0.5f)
        {
            return eye;
        }

        var forward = camera.Forward;
        var focused = new Vector3<double>(eye.X + forward.X * camera.FocusDistance, eye.Y, eye.Z + forward.Z * camera.FocusDistance);
        var heading = new Vector3F((float)forward.X, 0, (float)forward.Z);
        if (heading.Length() < 1e-3f)
        {
            return focused;
        }

        heading = Vector3F.Normalize(heading);
        var below = (float)((float.IsFinite(PlaneY) ? PlaneY : 0) - eye.Y);
        Vector3F? nearest = null;
        for (int edge = 0; edge < 2; edge++)
        {
            var row = edge == 0 ? 0f : camera.Height;
            var ray = PickRay.FromCamera(camera, new Vector2F(camera.Width / 2f, row)).Ray;
            if (Math.Abs(ray.Direction.Y) < 1e-6f)
            {
                continue;
            }

            var along = (below - ray.Position.Y) / ray.Direction.Y;
            if (along < 0)
            {
                continue;
            }

            var met = ray.Position + ray.Direction * along;
            if (nearest is not { } kept || Vector3F.Dot(met, heading) < Vector3F.Dot(kept, heading))
            {
                nearest = met;
            }
        }

        if (nearest is not { } near)
        {
            return focused;
        }

        var center = near + heading * (WholeShare * FlatReach(camera));
        return new Vector3<double>(eye.X + center.X, eye.Y, eye.Z + center.Z);
    }

    private float FlatReach(CameraBase camera)
    {
        var projection = camera.ProjectionMatrix;
        var slope = Math.Max(Math.Abs((float)camera.Forward.Y), 1e-3f);
        var reach = FlatReachPerShown * Math.Max(1 / projection.M11, 1 / projection.M22 / slope);
        return Math.Min(Math.Max(reach, Cell * LeastReachInCells), Math.Min(camera.ZFar * 0.9f, camera.OrthoFar * 0.6f));
    }

    private static float EdgeOn(CameraBase camera, float below, float cell)
    {
        var forward = camera.Forward.As<float>();
        if (camera.ProjectionMatrix.M44 > 0.5f)
        {
            return 0;
        }

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
        entity.AddComponent(new MeshData { Mesh = mesh, DepthTestEnabled = true, DepthWriteEnabled = false });
        entity.AddComponent(new Material { HighlightColor = Vector4F.One, Transparency = 1 });
        return entity;
    }

    private static Mesh Segment(Vector3<double> along)
    {
        var mesh = new Mesh(PrimitiveType.LineList);
        mesh.SetPoints(new List<Vector3<double>> { -along, along });
        mesh.SetIndices(new List<int> { 0, 1 });
        return mesh;
    }

    private static Mesh UnitGrid()
    {
        var points = new List<Vector3<double>>();
        var indices = new List<int>();
        for (int i = -EdgeLines; i <= EdgeLines; i++)
        {
            indices.Add(points.Count);
            points.Add(new Vector3<double>(i, 0, -EdgeLines));
            indices.Add(points.Count);
            points.Add(new Vector3<double>(i, 0, EdgeLines));
            indices.Add(points.Count);
            points.Add(new Vector3<double>(-EdgeLines, 0, i));
            indices.Add(points.Count);
            points.Add(new Vector3<double>(EdgeLines, 0, i));
        }

        var mesh = new Mesh(PrimitiveType.LineList);
        mesh.SetPoints(points);
        mesh.SetIndices(indices);
        return mesh;
    }

    /// <summary>Where the grid lies for <paramref name="camera"/>, round <see cref="CenterFor"/>: in a perspective it
    /// reaches sixty times the eye's height above the grid, in a flat view one and a half times the view's width or the
    /// ground its height covers, whichever is longer; never less than two hundred cells and never past nine tenths of the
    /// far plane - in a flat view, six tenths of its own, the grid's center standing ahead - fading out toward its edge in
    /// either, as ground does toward a horizon.</summary>
    public GroundGridLayout LayoutFor(CameraBase camera)
    {
        var eye = camera.WorldPosition;
        var center = CenterFor(camera);
        var cell = Cell;
        var below = (float)((float.IsFinite(PlaneY) ? PlaneY : 0) - eye.Y);
        var reach = camera.ProjectionMatrix.M44 > 0.5f
            ? FlatReach(camera)
            : Math.Min(Math.Max(Math.Abs(below) * ReachPerHeight, cell * LeastReachInCells), camera.ZFar * 0.9f);
        var offset = new Vector2F((float)(center.X - eye.X), (float)(center.Z - eye.Z));
        var placement = Matrix4x4.CreateScale<float>(reach) * Matrix4x4.CreateRotationX<float>(MathF.PI / 2)
                        * Matrix4x4.CreateTranslation<float>(offset.X, below, offset.Y);
        var axes = new Vector2F(Math.Clamp((float)-eye.X, offset.X - 2 * reach, offset.X + 2 * reach),
            Math.Clamp((float)-eye.Z, offset.Y - 2 * reach, offset.Y + 2 * reach));
        return new GroundGridLayout(placement, axes, reach, cell) { Center = offset };
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
