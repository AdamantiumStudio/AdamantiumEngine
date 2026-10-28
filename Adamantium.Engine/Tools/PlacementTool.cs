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
/// surface, on the ground grid wherever it is seen. Where the pointer then leaves the scene - above the horizon, past
/// the grid - the ghost stays where it last touched it; from an eye standing on the ground, which the pointer never
/// meets in front of it, the ghost goes on the ground under what the pointer shows at the camera's focus. A click
/// places it; with Shift held the tool stays to place another, else it is done. Esc gives up; the right button turns the
/// camera.
/// </summary>
public sealed class PlacementTool : ToolProcessor
{
    private static readonly Vector4F GhostTint = new(0.35f, 0.75f, 1f, 0.45f);
    private Vector3<double>? lastTouch;
    private readonly float lowest;
    private bool finished;

    /// <param name="ghost">What is placed, standing at its origin; it is only drawn and never joins the world. Null draws
    /// no ghost, only the <see cref="Icon"/> if there is one: the tool then picks what to place onto, <see cref="Under"/>,
    /// as with <see cref="NeedsSomethingUnder"/>.</param>
    public PlacementTool(Entity ghost)
    {
        Ghost = ghost;
        lowest = ghost == null ? 0 : LowestPoint(ghost);
    }

    public Entity Ghost { get; }

    /// <summary>Where the ghost's origin stands now, in the world, or null while the pointer finds no place for it in view.</summary>
    public Vector3<double>? Position { get; private set; }

    /// <summary>The scene entity the ghost rests on - the part under the pointer, or with Alt held the whole it belongs
    /// to, its root - or null on the ground. Picked for <see cref="NeedsSomethingUnder"/>, it is outlined as hovered.</summary>
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

        if (Tools.IsPointerTaken || input.IsMouseButtonDown(MouseButton.Right))
        {
            return;
        }

        var ray = PickRay.FromCamera(camera, input.RelativePosition);
        var hit = Picking.Pick(Tools.EntityWorld.RootEntities, ray, PickMode.Triangles, withDisabled: true);
        Vector3<double> point;
        if (hit.IsHit)
        {
            point = hit.Point.As<double>() + camera.WorldPosition;
            lastTouch = point;
            Under = Modifier(Keys.Alt) ? RootOf(hit.Entity) : hit.Entity;
            if (NeedsSomethingUnder)
            {
                Tools.Hovered = Under;
            }
        }
        else if (Ground() is not { } grid)
        {
            return;
        }
        else
        {
            var planeY = GroundY(grid);
            var layout = grid.LayoutFor(camera);
            var reach = layout.Reach + layout.Center.Length();
            var eye = camera.WorldPosition;
            var flat = camera.ProjectionMatrix.M44 > 0.5f;
            var eyeOnGround = !flat && Math.Abs(eye.Y - planeY) < camera.ZNear * 2;
            if (!eyeOnGround && OnGround(ray.Ray, eye, planeY, reach, out point))
            {
                lastTouch = point;
            }
            else if (!eyeOnGround && !NeedsSomethingUnder && lastTouch is { } kept && InView(kept, camera))
            {
                point = kept;
            }
            else
            {
                point = UnderFocus(ray.Ray, camera, planeY, ViewDepth(camera));
                if (!InView(point, camera))
                {
                    return;
                }
            }
        }

        var position = RestOn(point, lowest);
        Position = position;
        var cameras = Tools.Observatory.CurrentCameras;
        var iconPixels = Find<EntityIcons>()?.Pixels ?? EntityIcons.DefaultPixels;
        for (int i = 0; i < cameras.Count; i++)
        {
            if (Ghost != null)
            {
                PlaceGhost(Ghost, Matrix4x4.CreateTranslation<float>((position - cameras[i].WorldPosition).As<float>()), cameras[i]);
            }

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
            if (Ghost != null)
            {
                overlay.DrawGhost(Ghost, GhostTint);
            }

            if (Icon != null)
            {
                overlay.DrawFlatInScene(Icon);
            }
        }
    }

    /// <summary>
    /// Where a ray in render space - its origin taken from <paramref name="eye"/> - meets the plane y =
    /// <paramref name="planeY"/> in the world, no further across it from the eye than <paramref name="reach"/>, as far
    /// as the ground grid lies; a flat view's rays start far behind the eye.
    /// </summary>
    public static bool OnGround(in Ray<float> ray, Vector3<double> eye, double planeY, float reach, out Vector3<double> point)
    {
        point = default;
        if (Math.Abs(ray.Direction.Y) < 1e-6f)
        {
            return false;
        }

        var start = ray.Position.As<double>() + eye;
        var along = (planeY - start.Y) / ray.Direction.Y;
        if (along < 0)
        {
            return false;
        }

        var met = start + ray.Direction.As<double>() * along;
        if (Math.Abs(met.X - eye.X) > reach || Math.Abs(met.Z - eye.Z) > reach)
        {
            return false;
        }

        point = met;
        return true;
    }

    /// <summary>
    /// Where a ray in render space reaches <paramref name="depth"/> ahead of the eye along its view, dropped straight
    /// onto the plane y = <paramref name="planeY"/>: the ground under what the pointer shows there, for a ray that runs
    /// along the ground or above it and meets it far off or never. A ray meeting the ground at that depth gives that
    /// very point, so the ground near and this beyond join.
    /// </summary>
    public static Vector3<double> UnderFocus(in Ray<float> ray, CameraBase camera, double planeY, double depth)
    {
        var forward = camera.Forward.As<float>();
        var facing = Vector3F.Dot(ray.Direction, forward);
        var along = facing > 1e-6f ? ((float)depth - Vector3F.Dot(ray.Position, forward)) / facing : (float)depth;
        var met = ray.Position.As<double>() + ray.Direction.As<double>() * along + camera.WorldPosition;
        return new Vector3<double>(met.X, planeY, met.Z);
    }

    private static Entity RootOf(Entity entity)
    {
        while (entity.Owner != null)
        {
            entity = entity.Owner;
        }

        return entity;
    }

    private static double ViewDepth(CameraBase camera)
    {
        var depth = camera.Type.IsThirdPerson() && camera.Subject != null
            ? (camera.Subject.GetCenterAbsolute() - camera.WorldPosition).Length()
            : camera.FocusDistance;
        return Math.Max(depth, camera.ZNear * 2);
    }

    private static bool InView(Vector3<double> point, CameraBase camera)
    {
        var inRender = ScreenSpace.InRender(point, camera);
        if (Vector3F.Dot(inRender, camera.Forward.As<float>()) <= 0 && camera.ProjectionMatrix.M44 < 0.5f)
        {
            return false;
        }

        var pixel = ScreenSpace.ToPixel(inRender, camera);
        return pixel.X >= 0 && pixel.Y >= 0 && pixel.X <= camera.Width && pixel.Y <= camera.Height;
    }

    /// <summary>Where an object's origin stands for its lowest point, <paramref name="lowest"/> in its own space, to rest
    /// on <paramref name="point"/>; up is -Y, so the lowest point has the largest y.</summary>
    public static Vector3<double> RestOn(Vector3<double> point, float lowest)
    {
        return new Vector3<double>(point.X, point.Y - lowest, point.Z);
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
            Span<Vector3<double>> corners = stackalloc Vector3<double>[OrientedBoundingBox<double>.CornerCount];
            mesh.Bounds.GetCorners(corners);
            foreach (var corner in corners)
            {
                lowest = Math.Max(lowest, Vector3F.TransformCoordinate(corner.As<float>(), world).Y);
            }
        }

        var children = part.Dependencies;
        for (int i = 0; i < children.Count; i++)
        {
            Lowest(children[i], world, ref lowest);
        }
    }
}
