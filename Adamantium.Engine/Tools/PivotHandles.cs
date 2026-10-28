using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Templates.Tools;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Handles standing on a target's pivot: arrows and a center that move the pivot, rings that turn its axes. The target
/// itself stays where it is.
/// </summary>
public class PivotHandles : Handles
{
    private const float Length = 2;

    private readonly Entity moveRight;
    private readonly Entity moveUp;
    private readonly Entity moveForward;
    private readonly Entity rightOrbit;
    private readonly Entity upOrbit;
    private readonly Entity forwardOrbit;
    private readonly Entity point;
    private readonly Entity central;
    private readonly RingDrag ringDrag = new();
    private Vector3<double> anchor;
    private Vector3<double> startPivot;
    private QuaternionF startPivotRotation;
    private Vector3F direction;
    private Vector3F startVector;
    private float startAlong;
    private Kind kind;

    public PivotHandles()
        : base(new PivotToolTemplate(1, new Vector3F(2)).BuildEntity(null, nameof(PivotHandles)))
    {
        moveRight = Shape.Get("MoveRight");
        moveUp = Shape.Get("MoveUp");
        moveForward = Shape.Get("MoveForward");
        rightOrbit = Shape.Get("RightOrbit");
        upOrbit = Shape.Get("UpOrbit");
        forwardOrbit = Shape.Get("ForwardOrbit");
        point = Shape.Get("PivotPoint");
        central = Shape.Get("CentralManipulator");
        rightOrbit.IgnoreInCollisionDetection = true;
        upOrbit.IgnoreInCollisionDetection = true;
        forwardOrbit.IgnoreInCollisionDetection = true;
    }

    private enum Kind
    {
        Axis,
        Plane,
        Turn
    }

    public override void Place(Entity target, Camera camera)
    {
        var at = PivotInWorld(target);
        var scale = Pixels * UnitsPerPoint(camera, at) / Length;
        var axes = AxesOf(target);
        PlaceShape(camera, at, axes, scale);
        HideArmsFacingEye(moveRight, moveUp, moveForward, axes, at, camera);

        var facing = Matrix4x4.CreateScale<float>(scale)
                     * Matrix4x4.CreateFromQuaternion<float>(FacingCamera(camera))
                     * Matrix4x4.CreateTranslation<float>(InRender(at, camera));
        PlacePart(point, facing, camera);
        PlacePart(central, facing, camera);
    }

    /// <summary>The rings by what of them is drawn nearest the pointer on screen, the rest as usual; the nearer wins.</summary>
    public override PickHit Pick(in PickRay ray, float aperture)
    {
        var ring = default(PickHit);
        var gap = aperture;
        NearestOnRing(ray, rightOrbit, Vector3F.UnitX, ref ring, ref gap);
        NearestOnRing(ray, upOrbit, Vector3F.UnitY, ref ring, ref gap);
        NearestOnRing(ray, forwardOrbit, Vector3F.UnitZ, ref ring, ref gap);
        return PickHit.Nearest(base.Pick(ray, aperture), ring);
    }

    public override void BeginDrag(Entity target, Entity handle, in PickRay ray)
    {
        anchor = PivotInWorld(target);
        startPivot = target.Transform.Pivot;
        startPivotRotation = target.Transform.PivotRotation;
        var origin = InRender(anchor, ray.Camera);
        var axes = AxesOf(target);

        if (IsPartOf(handle, moveRight) || IsPartOf(handle, moveUp) || IsPartOf(handle, moveForward))
        {
            kind = Kind.Axis;
            direction = Axis(axes, IsPartOf(handle, moveRight) ? Vector3F.UnitX
                : IsPartOf(handle, moveUp) ? Vector3F.UnitY
                : Vector3F.UnitZ);
            AlongLine(ray.Ray, origin, direction, out startAlong);
            return;
        }

        if (handle == rightOrbit || handle == upOrbit || handle == forwardOrbit)
        {
            kind = Kind.Turn;
            var local = handle == rightOrbit ? Vector3F.UnitX
                : handle == upOrbit ? Vector3F.UnitY
                : Vector3F.UnitZ;
            direction = Axis(axes, local);
            ringDrag.Begin(ray, handle, local, direction, origin);
            return;
        }

        kind = Kind.Plane;
        direction = Vector3F.Normalize(ray.Camera.Forward.As<float>());
        OnPlane(ray.Ray, origin, direction, out var hit);
        startVector = hit - origin;
    }

    public override void Drag(Entity target, in PickRay ray)
    {
        var origin = InRender(anchor, ray.Camera);
        switch (kind)
        {
            case Kind.Axis:
            {
                if (AlongLine(ray.Ray, origin, direction, out var along))
                {
                    target.Transform.Pivot = startPivot + ToParent(target, direction * (along - startAlong)).As<double>();
                }

                break;
            }
            case Kind.Plane:
            {
                if (OnPlane(ray.Ray, origin, direction, out var hit))
                {
                    target.Transform.Pivot = startPivot + ToParent(target, hit - origin - startVector).As<double>();
                }

                break;
            }
            default:
            {
                var inParent = Vector3F.Normalize(ToParent(target, direction));
                var turn = Quaternion.CreateFromAxisAngle<float>(inParent, ringDrag.Angle(ray, origin));
                target.Transform.PivotRotation = QuaternionF.Multiply(turn, startPivotRotation);
                break;
            }
        }
    }

    public override void Draw(EditorOverlayProcessor overlay)
    {
        DrawOrbit(overlay, rightOrbit, Vector3F.UnitX);
        DrawOrbit(overlay, upOrbit, Vector3F.UnitY);
        DrawOrbit(overlay, forwardOrbit, Vector3F.UnitZ);
        overlay.DrawInScene(moveRight);
        overlay.DrawInScene(moveUp);
        overlay.DrawInScene(moveForward);
        overlay.DrawInScene(point);
        overlay.DrawInScene(central);
    }

    public override void Highlight(Entity handle)
    {
        base.Highlight(AxisOf(handle) ?? handle);
    }

    private static QuaternionF AxesOf(Entity target)
    {
        return RotationInWorld(target, target.Transform.PivotRotation);
    }

    private Entity AxisOf(Entity handle)
    {
        if (IsPartOf(handle, moveRight))
        {
            return moveRight;
        }

        if (IsPartOf(handle, moveUp))
        {
            return moveUp;
        }

        return IsPartOf(handle, moveForward) ? moveForward : null;
    }
}
