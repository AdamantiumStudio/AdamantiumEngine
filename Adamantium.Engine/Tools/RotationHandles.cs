using System;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Templates.Tools;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// Rings that turn a target about a world axis or about the view, and a ball inside them that turns it freely. The
/// target turns about its pivot. A ring turns by the pointer's way along it, a radian per radius on screen, as in Unity.
/// </summary>
public class RotationHandles : Handles
{
    private const float Radius = 2;

    private readonly Entity rightOrbit;
    private readonly Entity upOrbit;
    private readonly Entity forwardOrbit;
    private readonly Entity viewOrbit;
    private readonly Entity viewCircle;
    private readonly Entity ball;
    private readonly RingDrag ringDrag = new();
    private Vector3<double> anchor;
    private QuaternionF startRotation;
    private Vector3F axis;
    private Vector3F startVector;
    private Vector3F viewForward;
    private float ballRadius;
    private bool free;

    public RotationHandles()
        : base(new RotationToolTemplate(2, new Vector3F(2), 128).BuildEntity(null, nameof(RotationHandles)))
    {
        rightOrbit = Shape.Get("RightAxisOrbit");
        upOrbit = Shape.Get("UpAxisOrbit");
        forwardOrbit = Shape.Get("ForwardAxisOrbit");
        viewOrbit = Shape.Get("CurrentViewManipulator");
        viewCircle = Shape.Get("CurrentViewCircle");
        ball = Shape.Get("CentralManipulator");
    }

    public override void Place(Entity target, Camera camera)
    {
        var at = PivotInWorld(target);
        var scale = Pixels * UnitsPerPoint(camera, at) / Radius;
        PlaceShape(camera, at, QuaternionF.Identity, scale);

        var facing = Matrix4x4.CreateScale<float>(scale)
                     * Matrix4x4.CreateFromQuaternion<float>(FacingCamera(camera))
                     * Matrix4x4.CreateTranslation<float>(InRender(at, camera));
        PlacePart(viewOrbit, facing, camera);
        PlacePart(viewCircle, facing, camera);
    }

    /// <summary>
    /// What is drawn nearest the pointer on screen: the shown part of a ring, the black circle for the ball, else the
    /// ball anywhere inside that circle.
    /// </summary>
    public override PickHit Pick(in PickRay ray, float aperture)
    {
        var nearest = default(PickHit);
        var gap = aperture;
        NearestOnRing(ray, rightOrbit, Vector3F.UnitX, ref nearest, ref gap);
        NearestOnRing(ray, upOrbit, Vector3F.UnitY, ref nearest, ref gap);
        NearestOnRing(ray, forwardOrbit, Vector3F.UnitZ, ref nearest, ref gap);
        NearestOnRing(ray, viewOrbit, null, ref nearest, ref gap);
        NearestOnRing(ray, viewCircle, null, ref nearest, ref gap);

        if (nearest.IsHit)
        {
            return nearest.Entity == viewCircle ? new PickHit(ball, nearest.Point, nearest.Depth) : nearest;
        }

        return InsideCircle(ray);
    }

    public override void BeginDrag(Entity target, Entity handle, in PickRay ray)
    {
        anchor = PivotInWorld(target);
        startRotation = target.Transform.Rotation;
        viewForward = Vector3F.Normalize(ray.Camera.Forward.As<float>());
        var origin = InRender(anchor, ray.Camera);

        free = handle == ball;
        if (free)
        {
            ballRadius = Pixels * UnitsPerPoint(ray.Camera, anchor);
            startVector = OnBall(ray.Ray, origin);
            return;
        }

        axis = handle == rightOrbit ? Vector3F.UnitX
            : handle == upOrbit ? Vector3F.UnitY
            : handle == forwardOrbit ? Vector3F.UnitZ
            : viewForward;
        ringDrag.Begin(ray, handle, handle == viewOrbit ? null : axis, axis, origin);
    }

    public override void Drag(Entity target, in PickRay ray)
    {
        var origin = InRender(anchor, ray.Camera);
        Vector3F turnAxis;
        float angle;

        if (free)
        {
            var now = OnBall(ray.Ray, origin);
            turnAxis = Vector3F.Cross(startVector, now);
            if (turnAxis.LengthSquared() < 1e-12f)
            {
                return;
            }

            turnAxis = Vector3F.Normalize(turnAxis);
            angle = AngleAbout(startVector, now, turnAxis);
        }
        else
        {
            turnAxis = axis;
            angle = ringDrag.Angle(ray, origin);
        }

        var inParent = Vector3F.Normalize(ToParent(target, turnAxis));
        target.Transform.Rotation = QuaternionF.Multiply(Quaternion.CreateFromAxisAngle<float>(inParent, angle), startRotation);
    }

    public override void Draw(EditorOverlayProcessor overlay)
    {
        overlay.DrawRing(viewCircle, false);
        overlay.DrawRing(viewOrbit, false);
        DrawOrbit(overlay, rightOrbit, Vector3F.UnitX);
        DrawOrbit(overlay, upOrbit, Vector3F.UnitY);
        DrawOrbit(overlay, forwardOrbit, Vector3F.UnitZ);
    }

    public override void Highlight(Entity handle)
    {
        base.Highlight(handle == ball ? viewCircle : handle);
    }

    private PickHit InsideCircle(in PickRay pick)
    {
        var world = viewCircle.Transform.GetMetadata(pick.Camera).WorldMatrixF;
        var center = world.TranslationVector;
        var bounds = viewCircle.GetComponent<MeshData>().Mesh.Bounds;
        var radius = Vector3F.TransformNormal(new Vector3F((float)bounds.HalfExtent.X, 0, 0), world).Length();
        if (!OnPlane(pick.Ray, center, Vector3F.Normalize(center), out var hit) || (hit - center).Length() > radius)
        {
            return default;
        }

        return new PickHit(ball, hit, Vector3F.Dot(hit - pick.Ray.Position, pick.Ray.Direction));
    }

    private Vector3F OnBall(in Ray<float> ray, Vector3F center)
    {
        if (!OnPlane(ray, center, viewForward, out var hit))
        {
            return viewForward;
        }

        var across = hit - center;
        var squared = across.LengthSquared();
        var ball = ballRadius * ballRadius;
        var height = squared <= ball / 2 ? Math.Sqrt(ball - squared) : ball / (2 * Math.Sqrt(squared));
        return across - viewForward * (float)height;
    }
}
