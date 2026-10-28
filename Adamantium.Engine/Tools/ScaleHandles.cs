using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.Templates.Tools;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Handles that scale a target about its pivot along its own axes: one axis, two at once, or all evenly.
/// </summary>
public class ScaleHandles : Handles
{
    private const float Length = 2;
    private const float Smallest = 0.01f;

    private readonly Entity right;
    private readonly Entity up;
    private readonly Entity forward;
    private readonly Entity rightUp;
    private readonly Entity rightForward;
    private readonly Entity upForward;
    private readonly Entity central;
    private Vector3<double> anchor;
    private Vector3F startScale;
    private Vector3F direction;
    private Vector3F scaled;
    private Vector3F viewRight;
    private Vector3F viewForward;
    private Vector3F startHit;
    private float startAlong;
    private float size;
    private bool even;

    public ScaleHandles()
        : base(new ScaleToolTemplate(1, new Vector3F(2)).BuildEntity(null, nameof(ScaleHandles)))
    {
        right = Shape.Get("RightAxis");
        up = Shape.Get("UpAxis");
        forward = Shape.Get("ForwardAxis");
        rightUp = Shape.Get("RightUpManipulator");
        rightForward = Shape.Get("RightForwardManipulator");
        upForward = Shape.Get("UpForwardManipulator");
        central = Shape.Get("CentralManipulator");
    }

    public override void Place(Entity target, Camera camera)
    {
        var at = PivotInWorld(target);
        var axes = AxesOf(target);
        PlaceShape(camera, at, axes, Pixels * UnitsPerPoint(camera, at) / Length);
        HideArmsFacingEye(right, up, forward, axes, at, camera);
        HideSquaresEdgeOn(rightUp, rightForward, upForward, axes, at, camera);
    }

    public override void BeginDrag(Entity target, Entity handle, in PickRay ray)
    {
        anchor = PivotInWorld(target);
        startScale = target.Transform.ScaleFactor;
        var origin = InRender(anchor, ray.Camera);

        even = handle == central;
        if (even)
        {
            var facing = FacingCamera(ray.Camera);
            viewRight = Axis(facing, Vector3F.UnitX);
            viewForward = Axis(facing, Vector3F.UnitZ);
            size = Pixels * UnitsPerPoint(ray.Camera, anchor);
            OnPlane(ray.Ray, origin, viewForward, out startHit);
            return;
        }

        var axes = AxesOf(target);
        var x = Axis(axes, Vector3F.UnitX);
        var y = Axis(axes, Vector3F.UnitY);
        var z = Axis(axes, Vector3F.UnitZ);

        if (IsPartOf(handle, right))
        {
            (direction, scaled) = (x, Vector3F.UnitX);
        }
        else if (IsPartOf(handle, up))
        {
            (direction, scaled) = (y, Vector3F.UnitY);
        }
        else if (IsPartOf(handle, forward))
        {
            (direction, scaled) = (z, Vector3F.UnitZ);
        }
        else if (handle == rightUp)
        {
            (direction, scaled) = (Vector3F.Normalize(x + y), new Vector3F(1, 1, 0));
        }
        else if (handle == rightForward)
        {
            (direction, scaled) = (Vector3F.Normalize(x + z), new Vector3F(1, 0, 1));
        }
        else
        {
            (direction, scaled) = (Vector3F.Normalize(y + z), new Vector3F(0, 1, 1));
        }

        AlongLine(ray.Ray, origin, direction, out startAlong);
    }

    public override void Drag(Entity target, in PickRay ray)
    {
        var origin = InRender(anchor, ray.Camera);
        float factor;
        if (even)
        {
            if (!OnPlane(ray.Ray, origin, viewForward, out var hit))
            {
                return;
            }

            factor = 1 + Vector3F.Dot(hit - startHit, viewRight) / size;
            scaled = Vector3F.One;
        }
        else
        {
            if (Math.Abs(startAlong) < 1e-6f || !AlongLine(ray.Ray, origin, direction, out var along))
            {
                return;
            }

            factor = along / startAlong;
        }

        factor = Math.Max(factor, Smallest);
        var applied = Vector3F.One + scaled * (factor - 1);
        target.Transform.ScaleFactor = startScale * applied;
    }

    public override void Highlight(Entity handle)
    {
        base.Highlight(AxisOf(handle) ?? handle);
    }

    private static QuaternionF AxesOf(Entity target)
    {
        return RotationInWorld(target, target.Transform.Rotation);
    }

    private Entity AxisOf(Entity handle)
    {
        if (IsPartOf(handle, right))
        {
            return right;
        }

        if (IsPartOf(handle, up))
        {
            return up;
        }

        return IsPartOf(handle, forward) ? forward : null;
    }
}
