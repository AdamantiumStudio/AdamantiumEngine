using System;
using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.EntityServices;
using Adamantium.Engine.Templates.Tools;
using Adamantium.Multiverse.Input;
using Adamantium.Mathematics;

namespace Adamantium.Engine.Tools;

/// <summary>
/// The world axes in a corner of every output: a click on a ball looks along that axis, an arrow turns the view a
/// quarter, a drag orbits it, a double click on the center turns it back to where it started. It takes the pointer
/// before any tool does.
/// </summary>
public class OrientationCube : EditorProcessor
{
    private const float ClickSlack = 3;
    private const float DragDegreesPerPixel = 0.4f;
    private const float StepDegrees = 90;
    private const int RotationMilliseconds = 500;

    private readonly Dictionary<Entity, Vector3F> tiles = new();
    private readonly Entity cube;
    private readonly Entity home;
    private readonly Entity stepRight;
    private readonly Entity stepLeft;
    private readonly Entity stepUp;
    private readonly Entity stepDown;
    private Entity grabbed;
    private float traveled;

    public OrientationCube()
    {
        var template = new OrientationToolTemplate(100, new Vector3F(1), Quaternion.CreateFromAxisAngle<float>(Vector3F.Right, float.DegreesToRadians(180)));
        cube = template.BuildEntity(null, nameof(OrientationCube));

        for (var i = 0; i < OrientationToolTemplate.TileDirections.Length; ++i)
        {
            tiles[cube.Get(OrientationToolTemplate.TileName(i))] = OrientationToolTemplate.TileDirections[i].As<float>();
        }

        home = cube.Get(OrientationToolTemplate.HomeName);
        stepRight = cube.Get("StepRightManipulator");
        stepLeft = cube.Get("StepLeftManipulator");
        stepUp = cube.Get("StepUpManipulator");
        stepDown = cube.Get("StepDownManipulator");
    }

    public override void Update(AppTime appTime)
    {
        var observatory = Tools.Observatory;
        var cameras = observatory.CurrentCameras;
        cube.TraverseByLayer(current =>
        {
            for (int i = 0; i < cameras.Count; i++)
            {
                Place(current, cameras[i]);
                current.Transform.GetMetadata(cameras[i]).IsSelected = false;
            }
        }, true);

        if (observatory.PointerOutput is not { Camera: { } camera, Input: { CanLocatePointer: true } input })
        {
            return;
        }

        // Only the camera the user steers is turned by the cube; another one is only looked through.
        if (!observatory.Controls(camera))
        {
            if (grabbed != null)
            {
                input.HoldPointer(false);
                grabbed = null;
            }

            return;
        }

        if (grabbed != null)
        {
            Tools.TakePointer();
            grabbed.Transform.GetMetadata(camera).IsSelected = true;
            Drag(camera, input);
            return;
        }

        var hit = cube.Pick(PickRay.FromViewProjection(camera, input.RelativePosition, camera.UiProjection), PickMode.MeshColliders);
        if (!hit.IsHit)
        {
            return;
        }

        Tools.TakePointer();
        hit.Entity.Transform.GetMetadata(camera).IsSelected = true;

        if (!input.IsMouseButtonPressed(MouseButton.Left))
        {
            return;
        }

        if (hit.Entity == home && input.MouseClickCount(MouseButton.Left) >= 2)
        {
            camera.RotateAroundSelectedObject(QuaternionF.Identity, RotationMilliseconds);
        }

        grabbed = hit.Entity;
        traveled = 0;
        input.HoldPointer(true);
    }

    public override void DrawOverlay(EditorOverlayProcessor overlay)
    {
        overlay.DrawOnScreen(cube);
    }

    private void Drag(Camera camera, InputWormhole input)
    {
        var delta = input.RawMouseDelta;

        if (input.IsMouseButtonDown(MouseButton.Left) && delta.LengthSquared() > 0)
        {
            traveled += delta.Length();

            if (traveled > ClickSlack)
            {
                camera.CancelTravel();
                camera.RotateRelativeXY(delta.Y * DragDegreesPerPixel, -delta.X * DragDegreesPerPixel);
            }
        }

        if (!input.IsMouseButtonReleased(MouseButton.Left))
        {
            return;
        }

        input.HoldPointer(false);

        if (traveled <= ClickSlack)
        {
            Click(camera, grabbed);
        }

        grabbed = null;
    }

    private void Place(Entity current, Camera camera)
    {
        var scale = camera.PixelsPerPoint;
        var relativePosition = new Vector3F(camera.Width - 120 * scale, 120 * scale, 150f);
        var orientation = IsStepArrow(current) ? QuaternionF.Identity : QuaternionF.Conjugate(camera.Rotation);

        var world = Matrix4x4.CreateFromQuaternion<float>(current.Transform.Rotation) *
                    Matrix4x4.CreateTranslation<float>(current.Transform.Position.As<float>()) *
                    Matrix4x4.CreateScale<float>(current.Transform.Scale) *
                    Matrix4x4.CreateFromQuaternion<float>(orientation) *
                    Matrix4x4.CreateScale<float>(scale);

        var metadata = current.Transform.GetMetadata(camera);
        metadata.WorldMatrixF = world * Matrix4x4.CreateTranslation<float>(relativePosition);
        metadata.RelativePosition = relativePosition;
        // Shown only to a camera the user steers: another one is only looked through, and nothing would turn it.
        metadata.Enabled = Tools.Observatory.Controls(camera);
    }

    private static Vector3F UpOf(Vector3F forward)
    {
        return Math.Abs(Vector3F.Dot(Vector3F.Up, forward)) > 0.99f ? Vector3F.ForwardLH : Vector3F.Up;
    }

    private bool IsStepArrow(Entity entity)
    {
        return entity == stepRight || entity == stepLeft || entity == stepUp || entity == stepDown;
    }

    private QuaternionF StepOf(Entity arrow)
    {
        var quarter = float.DegreesToRadians(StepDegrees);

        if (arrow == stepRight)
        {
            return Quaternion.CreateFromAxisAngle<float>(Vector3F.Up, quarter);
        }

        if (arrow == stepLeft)
        {
            return Quaternion.CreateFromAxisAngle<float>(Vector3F.Up, -quarter);
        }

        if (arrow == stepUp)
        {
            return Quaternion.CreateFromAxisAngle<float>(Vector3F.Right, quarter);
        }

        if (arrow == stepDown)
        {
            return Quaternion.CreateFromAxisAngle<float>(Vector3F.Right, -quarter);
        }

        return QuaternionF.Identity;
    }

    private void Click(Camera camera, Entity part)
    {
        if (IsStepArrow(part))
        {
            camera.RotateAroundSelectedObject(camera.Rotation * StepOf(part), RotationMilliseconds);
            return;
        }

        if (!tiles.TryGetValue(part, out var ball))
        {
            return;
        }

        var forward = -Vector3F.Normalize(ball);
        camera.RotateAroundSelectedObject(Quaternion.CreateLookToLH<float>(forward, UpOf(forward)), RotationMilliseconds);
    }
}
