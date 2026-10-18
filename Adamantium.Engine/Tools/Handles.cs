using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// A set of handles a tool drags: their shape, where they stand for a target, and what dragging one does to it. The
/// shape keeps its size on screen whatever the distance.
/// </summary>
public abstract class Handles
{
    private const float FacingEye = 0.97f;

    private static readonly float SquareEdgeOn = MathF.Sqrt(1 - FacingEye * FacingEye);

    protected Handles(Entity shape)
    {
        Shape = shape;
    }

    public Entity Shape { get; }

    /// <summary>How big the handles look, in points: pixels at 100% scale.</summary>
    public float Pixels { get; set; } = 110;

    /// <summary>Whether these handles work on <paramref name="target"/>.</summary>
    public virtual bool AppliesTo(Entity target)
    {
        return target != null;
    }

    /// <summary>Stands the handles at <paramref name="target"/> as one camera sees them.</summary>
    public abstract void Place(Entity target, Camera camera);

    /// <summary>Starts dragging <paramref name="handle"/>, one of the shape's parts.</summary>
    public abstract void BeginDrag(Entity target, Entity handle, in PickRay ray);

    public abstract void Drag(Entity target, in PickRay ray);

    /// <summary>Draws the handles into one output's frame.</summary>
    public virtual void Draw(EditorOverlayProcessor overlay)
    {
        overlay.DrawInScene(Shape);
    }

    /// <summary>The part of the handles a press along <paramref name="ray"/> would take; by default the nearest along it.</summary>
    public virtual PickHit Pick(in PickRay ray, float aperture)
    {
        return Shape.Pick(ray, PickMode.Colliders | PickMode.Lines, aperture);
    }

    /// <summary>Lights up what dragging <paramref name="handle"/> would move; null lights up nothing.</summary>
    public virtual void Highlight(Entity handle)
    {
        Shape.TraverseInDepth(part => part.IsSelected = false);
        handle?.TraverseInDepth(part => part.IsSelected = true);
    }

    internal static Vector3 PivotOf(Entity target) => PivotInWorld(target);

    internal static Vector3F ParentVector(Entity target, Vector3F worldVector) => ToParent(target, worldVector);

    internal static QuaternionF WorldRotationOf(Entity target, QuaternionF local) => RotationInWorld(target, local);

    protected static Vector3 PivotInWorld(Entity target)
    {
        var pivot = target.Transform.Pivot;
        if (target.Owner == null)
        {
            return pivot;
        }

        return (Vector3)Vector3F.TransformCoordinate((Vector3F)pivot, target.Owner.Transform.GetWorldMatrixF());
    }

    protected static QuaternionF RotationInWorld(Entity target, QuaternionF local)
    {
        if (target.Owner == null)
        {
            return local;
        }

        var parent = target.Owner.Transform.GetWorldMatrixF();
        var basis = Matrix4x4F.Identity;
        basis.Right = Vector3F.Normalize(parent.Right);
        basis.Up = Vector3F.Normalize(parent.Up);
        basis.Forward = Vector3F.Normalize(parent.Forward);
        return QuaternionF.RotationMatrix(Matrix4x4F.RotationQuaternion(local) * basis);
    }

    protected static Vector3F ToParent(Entity target, Vector3F worldVector)
    {
        if (target.Owner == null)
        {
            return worldVector;
        }

        var parent = target.Owner.Transform.GetWorldMatrixF();
        Matrix4x4F.Invert(ref parent, out var inverse);
        return Vector3F.TransformNormal(worldVector, inverse);
    }

    protected static float UnitsPerPixel(CameraBase camera, Vector3 worldPoint)
    {
        return ScreenSpace.UnitsPerPixel(camera, worldPoint);
    }

    protected static float UnitsPerPoint(CameraBase camera, Vector3 worldPoint)
    {
        return ScreenSpace.UnitsPerPoint(camera, worldPoint);
    }

    protected static Vector3F InRender(Vector3 worldPoint, CameraBase camera)
    {
        return ScreenSpace.InRender(worldPoint, camera);
    }

    protected static Vector3F Axis(QuaternionF axes, Vector3F unit)
    {
        return Vector3F.Normalize(Vector3F.Transform(unit, axes));
    }

    protected static QuaternionF FacingCamera(CameraBase camera)
    {
        return ScreenSpace.Facing(camera);
    }

    protected static QuaternionF RotationBetween(Vector3F from, Vector3F to)
    {
        var cosine = Vector3F.Dot(from, to);
        if (cosine > 0.99999f)
        {
            return QuaternionF.Identity;
        }

        var axis = Vector3F.Cross(from, to);
        if (axis.LengthSquared() < 1e-10f)
        {
            axis = Math.Abs(from.X) < 0.9f ? Vector3F.Cross(from, Vector3F.UnitX) : Vector3F.Cross(from, Vector3F.UnitY);
        }

        return QuaternionF.RotationAxis(Vector3F.Normalize(axis), (float)Math.Acos(Math.Clamp(cosine, -1f, 1f)));
    }

    protected static void PlaceAt(Entity part, Vector3 worldPoint, float points, CameraBase camera)
    {
        var metadata = part.Transform.GetMetadata(camera);
        metadata.WorldMatrixF = Matrix4x4F.Scaling(points * UnitsPerPoint(camera, worldPoint))
                                * Matrix4x4F.Translation(InRender(worldPoint, camera));
        metadata.Enabled = true;
    }

    protected void PlaceShape(Camera camera, Vector3 worldPoint, QuaternionF axes, float scale)
    {
        var placement = Matrix4x4F.Scaling(scale)
                        * Matrix4x4F.RotationQuaternion(axes)
                        * Matrix4x4F.Translation(InRender(worldPoint, camera));
        Shape.TraverseInDepth(part => PlacePart(part, placement, camera));
    }

    protected static void PlacePart(Entity part, Matrix4x4F placement, CameraBase camera)
    {
        var metadata = part.Transform.GetMetadata(camera);
        metadata.WorldMatrixF = part.Transform.GetLocalMatrixF() * placement;
        metadata.Enabled = true;
    }

    protected static void HideArmsFacingEye(Entity right, Entity up, Entity forward, QuaternionF axes, Vector3 at, CameraBase camera)
    {
        var sight = Vector3F.Normalize(InRender(at, camera));
        HideIf(right, Math.Abs(Vector3F.Dot(Axis(axes, Vector3F.UnitX), sight)) >= FacingEye, camera);
        HideIf(up, Math.Abs(Vector3F.Dot(Axis(axes, Vector3F.UnitY), sight)) >= FacingEye, camera);
        HideIf(forward, Math.Abs(Vector3F.Dot(Axis(axes, Vector3F.UnitZ), sight)) >= FacingEye, camera);
    }

    protected static void DrawOrbit(EditorOverlayProcessor overlay, Entity orbit, Vector3F axis)
    {
        overlay.DrawRing(orbit, !FacesEye(orbit.Transform.GetMetadata(overlay.Camera).WorldMatrixF, axis));
    }

    /// <summary>
    /// Whether a ring about <paramref name="axis"/> faces the eye; <see cref="DrawOrbit"/> then draws all of it, else only
    /// the half on the eye's side of its center.
    /// </summary>
    protected static bool FacesEye(in Matrix4x4F world, Vector3F axis)
    {
        var sight = Vector3F.Normalize(world.TranslationVector);
        var normal = Vector3F.Normalize(Vector3F.TransformNormal(axis, world));
        return Math.Abs(Vector3F.Dot(normal, sight)) >= FacingEye;
    }

    /// <summary>
    /// The point of a ring's shown part nearest <paramref name="pick"/> on screen, taken when it passes closer than
    /// <paramref name="gap"/> pixels, which then narrows to it. <paramref name="drawnHalfAxis"/> is the ring's axis in
    /// its own space as given to <see cref="DrawOrbit"/>; null for a ring drawn whole.
    /// </summary>
    protected static void NearestOnRing(in PickRay pick, Entity ring, Vector3F? drawnHalfAxis, ref PickHit nearest, ref float gap)
    {
        var metadata = ring.Transform.GetMetadata(pick.Camera);
        if (!metadata.Enabled)
        {
            return;
        }

        var world = metadata.WorldMatrixF;
        var center = world.TranslationVector;
        var whole = drawnHalfAxis is not { } halfAxis || FacesEye(world, halfAxis);
        var mesh = ring.GetComponent<MeshData>().Mesh;
        var points = mesh.Points;
        var indices = mesh.Indices;
        var count = mesh.HasIndices ? indices.Length : points.Length;
        var ray = pick.Ray;

        for (int i = 0; i + 1 < count; i += 2)
        {
            var start = Vector3F.TransformCoordinate((Vector3F)points[mesh.HasIndices ? indices[i] : i], world);
            var end = Vector3F.TransformCoordinate((Vector3F)points[mesh.HasIndices ? indices[i + 1] : i + 1], world);
            if (!whole)
            {
                var startBehind = Vector3F.Dot(start - center, center);
                var endBehind = Vector3F.Dot(end - center, center);
                if (startBehind > 0 && endBehind > 0)
                {
                    continue;
                }

                if (startBehind > 0 || endBehind > 0)
                {
                    var cut = start + (end - start) * (startBehind / (startBehind - endBehind));
                    if (startBehind > 0)
                    {
                        start = cut;
                    }
                    else
                    {
                        end = cut;
                    }
                }
            }

            var distance = Collision.RayIntersectsLineSegment(ref ray, start, end, out var point);
            var depth = Vector3F.Dot(point - ray.Position, ray.Direction);
            if (depth < 0)
            {
                continue;
            }

            var pixels = distance / pick.PixelSize(depth);
            if (pixels < gap)
            {
                gap = pixels;
                nearest = new PickHit(ring, point, depth);
            }
        }
    }

    /// <summary>
    /// Turning by a ring: near it, by the angle the pointer goes round the ring's center in the ring's own plane, so
    /// following the ring round turns a whole turn a loop, tilted or not, however fast the pointer moves; past two radii,
    /// by the pointer's way along the ring's tangent where it left, as fast however far it is pulled. A ring seen nearly
    /// edge-on draws no circle to follow: it turns by the pointer's way along its tangent where it was grabbed, a radian
    /// per radius on screen, as in Unity.
    /// </summary>
    protected sealed class RingDrag
    {
        private const float EdgeOn = 0.25f;
        private const float FollowedRadii = 2;
        private const float NearCenterPixels = 4;

        private Vector2F startPixel;
        private Vector2F tangentOnScreen;
        private float radiusInPixels;
        private bool round;
        private Vector3F axisUnit;
        private Vector3F alongGrab;
        private Vector3F acrossGrab;
        private float ringRadius;
        private float lastTurn;
        private bool following;
        private Vector2F lastPixel;
        private Vector2F pixelsPerRadian;
        private float swept;

        /// <summary>
        /// Grabs <paramref name="ring"/>, turning about <paramref name="axis"/> in the camera-relative space, where
        /// <paramref name="ray"/> passes nearest its shown part. <paramref name="origin"/> is the ring's center there.
        /// </summary>
        public void Begin(in PickRay ray, Entity ring, Vector3F? drawnHalfAxis, Vector3F axis, Vector3F origin)
        {
            var grab = default(PickHit);
            var gap = float.MaxValue;
            NearestOnRing(ray, ring, drawnHalfAxis, ref grab, ref gap);
            startPixel = PointerPixel(ray, origin);
            tangentOnScreen = Vector2F.Zero;
            if (!grab.IsHit)
            {
                return;
            }

            var radial = grab.Point - origin;
            var unitsPerPixel = UnitsPerPixel(ray.Camera, (Vector3)origin + ray.Camera.WorldPosition);
            radiusInPixels = radial.Length() / unitsPerPixel;
            var tangent = Vector3F.Normalize(Vector3F.Cross(axis, radial)) * unitsPerPixel;
            var onScreen = ScreenSpace.ToPixel(grab.Point + tangent, ray.Camera) - ScreenSpace.ToPixel(grab.Point, ray.Camera);
            if (onScreen.LengthSquared() > 1e-6f)
            {
                tangentOnScreen = Vector2F.Normalize(onScreen);
            }

            var flat = ray.Camera.ProjectionMatrix.M44 > 0.5f;
            var sight = flat ? Vector3F.Normalize((Vector3F)ray.Camera.Forward) : Vector3F.Normalize(origin);
            axisUnit = Vector3F.Normalize(axis);
            round = Math.Abs(Vector3F.Dot(axisUnit, sight)) >= EdgeOn && radial.LengthSquared() > 1e-12f;
            if (!round)
            {
                return;
            }

            ringRadius = radial.Length();
            alongGrab = radial / ringRadius;
            acrossGrab = Vector3F.Cross(axisUnit, alongGrab);
            var grabbedTurn = TurnOf(ray.Ray, origin, out _);
            lastTurn = grabbedTurn ?? 0;
            following = grabbedTurn != null;
            pixelsPerRadian = PixelsPerRadian(lastTurn, origin, ray.Camera);
            lastPixel = startPixel;
            swept = 0;
        }

        /// <summary>The turn so far, in radians; 0 when the grab found no way along the ring on screen.</summary>
        public float Angle(in PickRay ray, Vector3F origin)
        {
            if (tangentOnScreen == Vector2F.Zero || radiusInPixels <= 0)
            {
                return 0;
            }

            var pixel = PointerPixel(ray, origin);
            if (!round)
            {
                return Vector2F.Dot(pixel - startPixel, tangentOnScreen) / radiusInPixels;
            }

            var step = 0f;
            var nearCenter = (pixel - ScreenSpace.ToPixel(origin, ray.Camera)).Length() < NearCenterPixels;
            if (nearCenter)
            {
                following = false;
            }
            else if (TurnOf(ray.Ray, origin, out var away) is { } turn && away <= ringRadius * FollowedRadii)
            {
                step = following ? MathF.IEEERemainder(turn - lastTurn, 2 * MathF.PI) : 0;
                lastTurn = following ? lastTurn + step : turn;
                following = true;
                pixelsPerRadian = PixelsPerRadian(turn, origin, ray.Camera);
            }
            else
            {
                var length = pixelsPerRadian.LengthSquared();
                step = length > 1e-6f ? Vector2F.Dot(pixel - lastPixel, pixelsPerRadian) / length : 0;
                following = false;
            }

            swept += step;
            lastPixel = pixel;
            return swept;
        }

        private float? TurnOf(in Ray ray, Vector3F origin, out float away)
        {
            away = float.MaxValue;
            if (!OnPlane(ray, origin, axisUnit, out var hit))
            {
                return null;
            }

            var inPlane = hit - origin;
            away = inPlane.Length();
            return away < 1e-6f ? null : MathF.Atan2(Vector3F.Dot(inPlane, acrossGrab), Vector3F.Dot(inPlane, alongGrab));
        }

        private Vector2F PixelsPerRadian(float turn, Vector3F origin, CameraBase camera)
        {
            const float step = 0.01f;
            var at = origin + (alongGrab * MathF.Cos(turn) + acrossGrab * MathF.Sin(turn)) * ringRadius;
            var on = origin + (alongGrab * MathF.Cos(turn + step) + acrossGrab * MathF.Sin(turn + step)) * ringRadius;
            return (ScreenSpace.ToPixel(on, camera) - ScreenSpace.ToPixel(at, camera)) / step;
        }

        private static Vector2F PointerPixel(in PickRay pick, Vector3F origin)
        {
            var ray = pick.Ray;
            var depth = Math.Max(Vector3F.Dot(origin - ray.Position, ray.Direction), 0);
            return ScreenSpace.ToPixel(ray.Position + ray.Direction * depth, pick.Camera);
        }
    }

    protected static void HideSquaresEdgeOn(Entity rightUp, Entity rightForward, Entity upForward, QuaternionF axes, Vector3 at, CameraBase camera)
    {
        var sight = Vector3F.Normalize(InRender(at, camera));
        HideIf(rightUp, Math.Abs(Vector3F.Dot(Axis(axes, Vector3F.UnitZ), sight)) < SquareEdgeOn, camera);
        HideIf(rightForward, Math.Abs(Vector3F.Dot(Axis(axes, Vector3F.UnitY), sight)) < SquareEdgeOn, camera);
        HideIf(upForward, Math.Abs(Vector3F.Dot(Axis(axes, Vector3F.UnitX), sight)) < SquareEdgeOn, camera);
    }

    protected static bool IsPartOf(Entity handle, Entity part)
    {
        for (var at = handle; at != null; at = at.Owner)
        {
            if (at == part)
            {
                return true;
            }
        }

        return false;
    }

    protected static bool AlongLine(in Ray ray, Vector3F origin, Vector3F direction, out float along)
    {
        var offset = ray.Position - origin;
        var b = Vector3F.Dot(ray.Direction, direction);
        var denominator = 1 - b * b;
        if (denominator < 1e-6f)
        {
            along = 0;
            return false;
        }

        along = (Vector3F.Dot(direction, offset) - b * Vector3F.Dot(ray.Direction, offset)) / denominator;
        return true;
    }

    protected static bool OnPlane(in Ray ray, Vector3F point, Vector3F normal, out Vector3F hit)
    {
        var facing = Vector3F.Dot(normal, ray.Direction);
        if (Math.Abs(facing) < 1e-6f)
        {
            hit = Vector3F.Zero;
            return false;
        }

        var distance = Vector3F.Dot(normal, point - ray.Position) / facing;
        hit = ray.Position + ray.Direction * distance;
        return distance >= 0;
    }

    protected static float AngleAbout(Vector3F from, Vector3F to, Vector3F axis)
    {
        return (float)Math.Atan2(Vector3F.Dot(axis, Vector3F.Cross(from, to)), Vector3F.Dot(from, to));
    }

    private static void HideIf(Entity part, bool hidden, CameraBase camera)
    {
        if (hidden)
        {
            part.TraverseInDepth(current => current.Transform.GetMetadata(camera).Enabled = false);
        }
    }
}
