using System;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Places a new object with the mouse: a ghost of it follows the pointer, resting on the surface under it or, past every
/// surface, on the ground grid. A click places it; with Shift held the tool stays to place another, else it is done. Esc
/// or a right click gives up; a right drag still turns the camera.
/// </summary>
public sealed class PlacementTool : ToolProcessor
{
    private static readonly Vector4F GhostTint = new(0.35f, 0.75f, 1f, 0.45f);
    private const float ClickSlop = 4;
    private readonly float lowest;
    private bool finished;
    private Vector2F rightPressedAt;
    private bool rightClicking;

    /// <param name="ghost">What is placed, standing at its origin; it is only drawn and never joins the world.</param>
    public PlacementTool(Entity ghost)
    {
        Ghost = ghost;
        lowest = LowestPoint(ghost);
    }

    public Entity Ghost { get; }

    /// <summary>Where the ghost's origin stands now, in the world, or null while the pointer finds no ground.</summary>
    public Vector3? Position { get; private set; }

    /// <summary>The scene entity the ghost rests on, or null on the ground.</summary>
    public Entity Under { get; private set; }

    /// <summary>Whether the ghost is placed only on a scene entity, as a camera riding on one is; a click on the ground
    /// then does nothing.</summary>
    public bool NeedsSomethingUnder { get; init; }

    /// <summary>An icon drawn where the ghost stands, facing the eye at the size of the scene's icons, for what the
    /// scene shows by an icon; null for none.</summary>
    public Entity Icon { get; init; }

    /// <summary>A click placed the ghost at <see cref="Position"/>, on <see cref="Under"/>; raised on the universe's
    /// thread.</summary>
    public event EventHandler Placed;

    /// <summary>The tool is done: it placed without Shift held, or Esc gave up; raised once, on the universe's thread.</summary>
    public event EventHandler Finished;

    public override void Update(AppTime appTime)
    {
        Position = null;
        Under = null;
        if (finished)
        {
            return;
        }

        // The keys come from whichever output has the keyboard, the pointer may be elsewhere.
        var keyboard = Tools.Observatory.KeyboardInput;
        if (keyboard != null && keyboard.IsKeyPressed(Keys.Escape))
        {
            Finish();
            return;
        }

        if (Tools.Observatory.PointerOutput is not { Camera: { } camera, Input: { CanLocatePointer: true } input })
        {
            return;
        }

        if (IsRightClick(input, camera))
        {
            Finish();
            return;
        }

        if (Tools.IsPointerTaken || input.IsMouseButtonDown(MouseButton.Right))
        {
            return;
        }

        var ray = PickRay.FromCamera(camera, input.RelativePosition);
        var hit = Picking.Pick(Tools.EntityWorld.RootEntities, ray, PickMode.Triangles, withDisabled: true);
        Vector3 point;
        if (hit.IsHit)
        {
            point = (Vector3)hit.Point + camera.WorldPosition;
            Under = hit.Entity;
        }
        else if (Ground() is not { } grid || !OnGround(ray.Ray, camera.WorldPosition, GroundY(grid), grid.LayoutFor(camera).Reach, out point))
        {
            return;
        }

        var position = RestOn(point, lowest);
        Position = position;
        var cameras = Tools.Observatory.CurrentCameras;
        var iconPixels = Find<EntityIcons>()?.Pixels ?? EntityIcons.DefaultPixels;
        for (int i = 0; i < cameras.Count; i++)
        {
            PlaceGhost(Ghost, Matrix4x4F.Translation((Vector3F)(position - cameras[i].WorldPosition)), cameras[i]);
            if (Icon != null)
            {
                EntityIcons.PlaceIcon(Icon, position, iconPixels, cameras[i]);
            }
        }

        if (!input.IsMouseButtonPressed(MouseButton.Left) || (NeedsSomethingUnder && Under == null))
        {
            return;
        }

        Placed?.Invoke(this, EventArgs.Empty);
        if (keyboard == null || !keyboard.IsKeyDown(Keys.Shift))
        {
            Finish();
        }
    }

    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        if (Position != null)
        {
            overlay.DrawGhost(Ghost, GhostTint);
            if (Icon != null)
            {
                overlay.DrawFlatInScene(Icon);
            }
        }
    }

    /// <summary>
    /// Where a ray in render space - its origin taken from <paramref name="eye"/> - meets the plane y =
    /// <paramref name="planeY"/> in the world, no further than <paramref name="reach"/> along it.
    /// </summary>
    public static bool OnGround(in Ray ray, Vector3 eye, double planeY, float reach, out Vector3 point)
    {
        point = default;
        if (Math.Abs(ray.Direction.Y) < 1e-6f)
        {
            return false;
        }

        var start = (Vector3)ray.Position + eye;
        var along = (planeY - start.Y) / ray.Direction.Y;
        if (along < 0 || along > reach)
        {
            return false;
        }

        point = start + (Vector3)ray.Direction * along;
        return true;
    }

    /// <summary>Where an object's origin stands for its lowest point, <paramref name="lowest"/> in its own space, to rest
    /// on <paramref name="point"/>; up is -Y, so the lowest point has the largest y.</summary>
    public static Vector3 RestOn(Vector3 point, float lowest)
    {
        return new Vector3(point.X, point.Y - lowest, point.Z);
    }

    protected override void OnDetached()
    {
        Position = null;
        Under = null;
        base.OnDetached();
    }

    private void Finish()
    {
        finished = true;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    // A right press let go about where it was pressed; one that moved further turned the camera.
    private bool IsRightClick(InputWormhole input, Camera camera)
    {
        if (input.IsMouseButtonPressed(MouseButton.Right))
        {
            rightPressedAt = input.RelativePosition;
            rightClicking = true;
        }

        if (!rightClicking)
        {
            return false;
        }

        if (Vector2F.Distance(input.RelativePosition, rightPressedAt) > ClickSlop * camera.PixelsPerPoint)
        {
            rightClicking = false;
            return false;
        }

        if (!input.IsMouseButtonReleased(MouseButton.Right))
        {
            return false;
        }

        rightClicking = false;
        return true;
    }

    private T Find<T>() where T : EditorProcessor
    {
        var processors = Tools.Processors;
        for (int i = 0; i < processors.Count; i++)
        {
            if (processors[i] is T processor)
            {
                return processor;
            }
        }

        return null;
    }

    // The ground is the grid's plane, as far as the grid is drawn; without a grid there is only what the pointer hits.
    private GroundGrid Ground() => Find<GroundGrid>();

    private static double GroundY(GroundGrid grid) => float.IsFinite(grid.PlaneY) ? grid.PlaneY : 0;

    // The ghost's own position is where it is placed, so its root stands at its origin; its parts keep theirs.
    private static Matrix4x4F LocalOf(Entity part, bool root)
    {
        var local = part.Transform.GetLocalMatrixF();
        if (root)
        {
            local.M41 = 0;
            local.M42 = 0;
            local.M43 = 0;
        }

        return local;
    }

    private static void PlaceGhost(Entity part, Matrix4x4F parentWorld, CameraBase camera, bool root = true)
    {
        var metadata = part.Transform.GetMetadata(camera);
        metadata.WorldMatrixF = LocalOf(part, root) * parentWorld;
        metadata.Enabled = true;
        var children = part.Dependencies;
        for (int i = 0; i < children.Count; i++)
        {
            PlaceGhost(children[i], metadata.WorldMatrixF, camera, false);
        }
    }

    private static float LowestPoint(Entity ghost)
    {
        var lowest = float.NegativeInfinity;
        Lowest(ghost, Matrix4x4F.Identity, ref lowest, true);
        return float.IsFinite(lowest) ? lowest : 0;
    }

    private static void Lowest(Entity part, Matrix4x4F parentWorld, ref float lowest, bool root = false)
    {
        var world = LocalOf(part, root) * parentWorld;
        if (part.GetComponent<MeshData>()?.Mesh is { HasPoints: true } mesh)
        {
            foreach (var corner in mesh.Bounds.GetCorners())
            {
                lowest = Math.Max(lowest, Vector3F.TransformCoordinate((Vector3F)corner, world).Y);
            }
        }

        var children = part.Dependencies;
        for (int i = 0; i < children.Count; i++)
        {
            Lowest(children[i], world, ref lowest);
        }
    }
}
