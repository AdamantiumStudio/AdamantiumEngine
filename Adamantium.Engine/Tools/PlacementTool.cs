using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Places a new object with the mouse: a ghost of it hangs on the pointer while the pointer is over the scene, resting
/// on the surface under it - a scene entity, or the ground grid as far as it is drawn. A view along the ground - a flat
/// side view, or an eye standing on it - never meets the ground ahead: there the ghost goes on the ground under what the
/// pointer shows at the camera's focus. With no surface under the pointer - above the horizon, past the grid - it hangs
/// on the pointer at the depth it last touched one in this view, paler, and a click places nothing: it goes down only
/// onto a surface. A click there places it; with Shift held the tool stays to place another, else it is done. Esc gives
/// up; the right button turns the camera.
/// </summary>
public sealed class PlacementTool : ToolProcessor
{
    private static readonly Vector4F GhostTint = new(0.35f, 0.75f, 1f, 0.45f);
    private static readonly Vector4F HangingTint = new(0.7f, 0.75f, 0.8f, 0.3f);
    private double? lastDepth;
    private CameraBase depthCamera;
    private readonly float lowest;
    private bool finished;
    private bool onSurface;
    private UniverseOutput guidedOutput;
    private Vector2F guidedPixel;
    private bool placeRequested;

    /// <param name="ghost">What is placed, standing at its origin; it is only drawn and never joins the world. Null draws
    /// no ghost, only the <see cref="Icon"/> if there is one: the tool then picks what to place onto, <see cref="Under"/>,
    /// as with <see cref="NeedsSomethingUnder"/>.</param>
    public PlacementTool(Entity ghost)
    {
        Ghost = ghost;
        lowest = ghost == null ? 0 : LowestPoint(ghost);
    }

    public Entity Ghost { get; }

    /// <summary>Where the ghost's origin stands now, in the world, or null while the pointer is off the scene.</summary>
    public Vector3<double>? Position { get; private set; }

    /// <summary>Whether a click places the ghost where it is: on a surface - on a scene entity for
    /// <see cref="NeedsSomethingUnder"/> - not hanging on the pointer.</summary>
    public bool CanPlace => Position != null && onSurface && (!NeedsSomethingUnder || Under != null);

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
        onSurface = false;
        if (finished)
        {
            return;
        }

        Follow();
        if (placeRequested && !finished)
        {
            placeRequested = false;
            Finish();
        }
    }

    private void Follow()
    {
        // The keys come from whichever output has the keyboard, the pointer may be elsewhere.
        var keyboard = Tools.Observatory.KeyboardInput;
        if (keyboard != null && keyboard.IsKeyPressed(Keys.Escape))
        {
            Finish();
            return;
        }

        CameraBase camera;
        Vector2F pointer;
        InputWormhole input = null;
        if (guidedOutput != null)
        {
            if (guidedOutput.Camera is not { } guidedCamera)
            {
                return;
            }

            camera = guidedCamera;
            pointer = guidedPixel;
        }
        else
        {
            if (Tools.Observatory.PointerOutput is not { Camera: { } pointerCamera, Input: { CanLocatePointer: true } pointerInput })
            {
                return;
            }

            if (Tools.IsPointerTaken || pointerInput.IsMouseButtonDown(MouseButton.Right))
            {
                return;
            }

            camera = pointerCamera;
            input = pointerInput;
            pointer = input.RelativePosition;
        }

        // Off the scene - over the ribbon it was picked from, say - there is nothing to hang it on, so nothing is drawn,
        // and it comes in under the pointer instead of waiting somewhere in the scene for it.
        if (pointer.X < 0 || pointer.Y < 0 || pointer.X > camera.Width || pointer.Y > camera.Height)
        {
            return;
        }

        var ray = PickRay.FromCamera(camera, pointer);
        if (OnSurface(Tools.EntityWorld.RootEntities, Ground(), camera, ray, out var point, out var hit))
        {
            onSurface = true;
            if (hit != null)
            {
                Under = Modifier(Keys.Alt) ? RootOf(hit) : hit;
                if (NeedsSomethingUnder)
                {
                    Tools.Hovered = Under;
                }
            }
        }
        else
        {
            if (!ReferenceEquals(depthCamera, camera))
            {
                lastDepth = null;
            }

            point = Along(ray.Ray, camera, lastDepth ?? GroundBelowDepth(camera, pointer) ?? ViewDepth(camera));
        }

        // A degenerate view - no size, a ray of NaN for a frame - places nothing and leaves nothing behind it.
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(point.Z))
        {
            onSurface = false;
            return;
        }

        if (onSurface)
        {
            var depth = Vector3<double>.Dot(point - camera.WorldPosition, camera.Forward.As<double>());
            lastDepth = IsFlat(camera) ? depth : Math.Max(depth, camera.ZNear * 2);
            depthCamera = camera;
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

        if (input == null)
        {
            if (placeRequested && CanPlace)
            {
                Placed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (!input.IsMouseButtonPressed(MouseButton.Left) || !CanPlace)
        {
            return;
        }

        Placed?.Invoke(this, EventArgs.Empty);
        if (keyboard == null || !keyboard.IsKeyDown(Keys.Shift))
        {
            Finish();
        }
    }

    /// <summary>
    /// Has the ghost follow a point given from outside instead of the pointer - <paramref name="pixel"/>, in pixels of
    /// <paramref name="output"/>'s surface, as a drag carried over the scene gives it, the pointer being the drag's
    /// meanwhile. A click then places nothing: <see cref="PlaceWhereGuided"/> does. Called on the universe's thread.
    /// </summary>
    public void Guide(UniverseOutput output, Vector2F pixel)
    {
        guidedOutput = output;
        guidedPixel = pixel;
    }

    /// <summary>
    /// With the next frame, places the ghost where <see cref="Guide"/> last led it - as a click there would, nothing
    /// where it could not go - and the tool is done. Called on the universe's thread.
    /// </summary>
    public void PlaceWhereGuided()
    {
        placeRequested = true;
    }

    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        if (Position != null)
        {
            if (Ghost != null)
            {
                overlay.DrawGhost(Ghost, CanPlace ? GhostTint : HangingTint);
            }

            if (Icon != null)
            {
                overlay.DrawFlatInScene(Icon);
            }
        }
    }

    private static bool OnSurface(IReadOnlyList<Entity> roots, GroundGrid ground, CameraBase camera, in PickRay ray,
        out Vector3<double> point, out Entity hit)
    {
        var picked = Picking.Pick(roots, ray, PickMode.Triangles, withDisabled: true);
        if (picked.IsHit)
        {
            point = picked.Point.As<double>() + camera.WorldPosition;
            hit = picked.Entity;
            return true;
        }

        hit = null;
        point = default;
        return ground != null && (OnTheGround(ground, ray.Ray, camera, out point) || AlongTheGround(ground, ray.Ray, camera, out point));
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
    /// Where a ray in render space reaches <paramref name="depth"/> ahead of the eye along its view, in the world: the
    /// point the pointer shows at that depth, where a ghost with no surface under it hangs. A flat view's rays start far
    /// behind the eye, so the depth is measured from the eye, not along the ray.
    /// </summary>
    public static Vector3<double> Along(in Ray<float> ray, CameraBase camera, double depth)
    {
        var forward = camera.Forward.As<float>();
        var facing = Vector3F.Dot(ray.Direction, forward);
        var along = facing > 1e-6f ? ((float)depth - Vector3F.Dot(ray.Position, forward)) / facing : (float)depth;
        return ray.Position.As<double>() + ray.Direction.As<double>() * along + camera.WorldPosition;
    }

    /// <summary>
    /// The depth ahead of the eye of the ground the pointer at <paramref name="pixel"/> first meets going straight down
    /// the view - the plane y = <paramref name="planeY"/>, no further across than <paramref name="reach"/> - or null
    /// when it meets none down there. A ghost brought in over the empty sky hangs at that depth: the size it will have
    /// when it lands, not the camera's focus, which can be right in front of the eye.
    /// </summary>
    public static double? FirstGroundBelow(CameraBase camera, Vector2F pixel, double planeY, float reach)
    {
        var eye = camera.WorldPosition;
        bool Meets(float y, out Vector3<double> point) =>
            OnGround(PickRay.FromCamera(camera, new Vector2F(pixel.X, y)).Ray, eye, planeY, reach, out point);

        var above = pixel.Y;
        var below = (float)camera.Height;
        if (Meets(above, out var met))
        {
            return Vector3<double>.Dot(met - eye, camera.Forward.As<double>());
        }

        if (!Meets(below, out met))
        {
            return null;
        }

        for (var i = 0; i < 16 && below - above > 0.5f; i++)
        {
            var middle = (above + below) / 2;
            if (Meets(middle, out var point))
            {
                below = middle;
                met = point;
            }
            else
            {
                above = middle;
            }
        }

        return Vector3<double>.Dot(met - eye, camera.Forward.As<double>());
    }

    /// <summary>
    /// Where a ray in render space reaches <paramref name="depth"/> ahead of the eye along its view, dropped straight
    /// onto the plane y = <paramref name="planeY"/>: the ground under what the pointer shows there, for a view along the
    /// ground, which meets it ahead nowhere.
    /// </summary>
    public static Vector3<double> UnderFocus(in Ray<float> ray, CameraBase camera, double planeY, double depth)
    {
        var met = Along(ray, camera, depth);
        return new Vector3<double>(met.X, planeY, met.Z);
    }

    // A view along the ground - a flat side view, whose rays run level, or an eye standing on it - meets it ahead
    // nowhere: the ground there is under what the pointer shows at the focus, while that is in view.
    private static bool AlongTheGround(GroundGrid grid, in Ray<float> ray, CameraBase camera, out Vector3<double> point)
    {
        point = default;
        var planeY = GroundY(grid);
        var level = IsFlat(camera)
            ? Math.Abs(ray.Direction.Y) < 1e-6f
            : Math.Abs(camera.WorldPosition.Y - planeY) < camera.ZNear * 2;
        if (!level)
        {
            return false;
        }

        point = UnderFocus(ray, camera, planeY, ViewDepth(camera));
        return InView(point, camera);
    }

    private static bool IsFlat(CameraBase camera) => camera.ProjectionMatrix.M44 > 0.5f;

    private static bool InView(Vector3<double> point, CameraBase camera)
    {
        var inRender = ScreenSpace.InRender(point, camera);
        if (Vector3F.Dot(inRender, camera.Forward.As<float>()) <= 0 && !IsFlat(camera))
        {
            return false;
        }

        var pixel = ScreenSpace.ToPixel(inRender, camera);
        return pixel.X >= 0 && pixel.Y >= 0 && pixel.X <= camera.Width && pixel.Y <= camera.Height;
    }

    // The ground under the pointer, as far as the grid is drawn. An eye standing on the ground meets it nowhere in front.
    private static bool OnTheGround(GroundGrid grid, in Ray<float> ray, CameraBase camera, out Vector3<double> point)
    {
        point = default;
        return SeesTheGround(grid, camera, out var planeY, out var reach) &&
               OnGround(ray, camera.WorldPosition, planeY, reach, out point);
    }

    private double? GroundBelowDepth(CameraBase camera, Vector2F pointer) =>
        Ground() is { } grid && SeesTheGround(grid, camera, out var planeY, out var reach)
            ? FirstGroundBelow(camera, pointer, planeY, reach)
            : null;

    private static bool SeesTheGround(GroundGrid grid, CameraBase camera, out double planeY, out float reach)
    {
        planeY = GroundY(grid);
        var layout = grid.LayoutFor(camera);
        reach = layout.Reach + layout.Center.Length();
        return IsFlat(camera) || Math.Abs(camera.WorldPosition.Y - planeY) >= camera.ZNear * 2;
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
