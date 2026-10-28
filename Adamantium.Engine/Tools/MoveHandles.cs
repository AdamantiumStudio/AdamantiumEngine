using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.Templates.Tools;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Arrows that move a target along an axis, squares that move it across a plane, and a center that moves it across the
/// view. The world's axes. Each square sits between its two arrows.
/// </summary>
public class MoveHandles : Handles
{
    private const float Length = 2;

    private readonly Entity right;
    private readonly Entity up;
    private readonly Entity forward;
    private readonly Entity rightUp;
    private readonly Entity rightForward;
    private readonly Entity upForward;
    private readonly Entity[] parts;
    private Vector3<double> anchor;
    private Vector3<double> startPosition;
    private Vector3F direction;
    private bool alongAxis;
    private float startAlong;
    private Vector3F startOffset;

    public MoveHandles()
        : base(new MoveToolTemplate(1, new Vector3F(2)).BuildEntity(null, nameof(MoveHandles)))
    {
        right = Shape.Get("RightAxis");
        up = Shape.Get("UpAxis");
        forward = Shape.Get("ForwardAxis");
        rightUp = Shape.Get("RightUpManipulator");
        rightForward = Shape.Get("RightForwardManipulator");
        upForward = Shape.Get("UpForwardManipulator");
        parts = [right, up, forward, rightUp, rightForward, upForward];
    }

    public override void Place(Entity target, Camera camera)
    {
        var at = PivotInWorld(target);
        PlaceShape(camera, at, QuaternionF.Identity, Pixels * UnitsPerPoint(camera, at) / Length);
        HideArmsFacingEye(right, up, forward, QuaternionF.Identity, at, camera);
        HideSquaresEdgeOn(rightUp, rightForward, upForward, QuaternionF.Identity, at, camera);
    }

    public override void BeginDrag(Entity target, Entity handle, in PickRay ray)
    {
        anchor = PivotInWorld(target);
        startPosition = target.Transform.Position;
        var origin = InRender(anchor, ray.Camera);

        alongAxis = IsPartOf(handle, right) || IsPartOf(handle, up) || IsPartOf(handle, forward);
        if (alongAxis)
        {
            direction = IsPartOf(handle, right) ? Vector3F.UnitX : IsPartOf(handle, up) ? Vector3F.UnitY : Vector3F.UnitZ;
            AlongLine(ray.Ray, origin, direction, out startAlong);
            return;
        }

        direction = IsPartOf(handle, rightUp) ? Vector3F.UnitZ
            : IsPartOf(handle, rightForward) ? Vector3F.UnitY
            : IsPartOf(handle, upForward) ? Vector3F.UnitX
            : Vector3F.Normalize(ray.Camera.Forward.As<float>());
        OnPlane(ray.Ray, origin, direction, out var hit);
        startOffset = hit - origin;
    }

    public override void Drag(Entity target, in PickRay ray)
    {
        var origin = InRender(anchor, ray.Camera);
        Vector3F moved;
        if (alongAxis)
        {
            if (!AlongLine(ray.Ray, origin, direction, out var along))
            {
                return;
            }

            moved = direction * (along - startAlong);
        }
        else
        {
            if (!OnPlane(ray.Ray, origin, direction, out var hit))
            {
                return;
            }

            moved = hit - origin - startOffset;
        }

        target.Transform.Position = startPosition + ToParent(target, moved).As<double>();
    }

    public override void Highlight(Entity handle)
    {
        base.Highlight(PartOf(handle) ?? handle);
    }

    private Entity PartOf(Entity handle)
    {
        foreach (var part in parts)
        {
            if (IsPartOf(handle, part))
            {
                return part;
            }
        }

        return null;
    }
}
