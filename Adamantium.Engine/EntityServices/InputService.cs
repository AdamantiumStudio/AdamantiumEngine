using System;
using System.Collections.Generic;
using System.Globalization;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.ECS.Components.Extensions;
using Adamantium.Engine.Tools;
using Adamantium.Multiverse;
using Adamantium.Multiverse.Input;
using Adamantium.Graphics.Core;
using Adamantium.Imaging;
using Adamantium.Mathematics;

namespace Adamantium.Engine.EntityServices;

public class InputService : EntityService
{
    private const double ZoomNotchesPerMeter = 8;
    private const float PinchNotchesPerDoubling = 4;

    // Mouse look turns by the distance moved, not by time: the turn a pixel gave at a 120 Hz frame.
    private const float LookPerPixel = 1f / 120;

    private Entity userControlledEntity;
    //private AudioManager audioManager;
    private Selection selection;
    private Observatory observatory;
    private readonly InputAction move;
    private readonly InputAction rise;
    private readonly InputAction turn;
    private readonly InputAction roll;
    private readonly InputAction look;
    private readonly InputAction zoom;
    private readonly InputAction twist;
    private readonly InputAction slowerTurn;
    private readonly InputAction fasterTurn;
    private readonly InputAction faster;
    private readonly InputAction slower;
    private readonly InputAction fasterDrag;
    private readonly InputAction slowerDrag;
    private readonly InputAction lessSensitive;
    private readonly InputAction moreSensitive;
    private readonly InputAction freeCamera;
    private readonly InputAction follow;
    private readonly InputAction followAlt;
    private readonly InputAction followLocked;
    private readonly InputAction lookBack;
    private readonly InputAction wireframe;
    private readonly InputAction screenshot;
    private bool panning;
    private Vector2F panFrom;
    private Camera panCamera;
    private double panDepth;

    public InputService(EntityWorld world) : base(world)
    {
        selection = EntityWorld.Satellites.Get<Selection>();
        observatory = EntityWorld.Satellites.Get<Observatory>();
        EntityWorld.EntityManager.EntityRemoved += EntityManagerEntityRemoved;
        //audioManager = new AudioManager();

        CameraActions = new InputActionMap("Camera");
        move = CameraActions.Add("Move", InputActionType.Vector,
            InputBinding.Vector(Key(Keys.W), Key(Keys.S), Key(Keys.A), Key(Keys.D)),
            InputBinding.Stick(Pad(GamepadAxis.LeftStickX), Pad(GamepadAxis.LeftStickY)));
        rise = CameraActions.Add("Rise", InputActionType.Axis, InputBinding.Axis(Key(Keys.E), Key(Keys.Q)));
        turn = CameraActions.Add("Turn", InputActionType.Vector,
            InputBinding.Vector(Key(Keys.UpArrow), Key(Keys.DownArrow), Key(Keys.LeftArrow), Key(Keys.RightArrow)),
            InputBinding.Vector(Key(Keys.NumPad8), Key(Keys.NumPad5), Key(Keys.NumPad4), Key(Keys.NumPad6)),
            InputBinding.Stick(Pad(GamepadAxis.RightStickX), Pad(GamepadAxis.RightStickY), scaleY: -1));
        roll = CameraActions.Add("Roll", InputActionType.Axis,
            InputBinding.Axis(Key(Keys.PageDown), Key(Keys.PageUp)),
            InputBinding.Axis(Pad(GamepadAxis.RightTrigger), Pad(GamepadAxis.LeftTrigger)));
        look = CameraActions.Add("Look", InputActionType.Vector,
            InputBinding.Stick(InputControl.Mouse(MouseAxis.DeltaX), InputControl.Mouse(MouseAxis.DeltaY),
                modifiers: InputControl.Mouse(MouseButton.Right)),
            InputBinding.Stick(InputControl.Gesture(GestureAxis.PanX), InputControl.Gesture(GestureAxis.PanY)));
        zoom = CameraActions.Add("Zoom", InputActionType.Axis,
            InputBinding.Control(InputControl.Mouse(MouseAxis.Wheel)),
            InputBinding.Control(InputControl.Gesture(GestureAxis.Pinch), PinchNotchesPerDoubling));
        twist = CameraActions.Add("Twist", InputActionType.Axis,
            InputBinding.Control(InputControl.Gesture(GestureAxis.Rotate)));
        slowerTurn = Button("SlowerTurn", Keys.Divide);
        fasterTurn = Button("FasterTurn", Keys.Multiply);
        faster = Button("Faster", Keys.Add);
        slower = Button("Slower", Keys.Subtract);
        fasterDrag = Button("FasterDrag", Keys.Digit0);
        slowerDrag = Button("SlowerDrag", Keys.Digit9);
        lessSensitive = Button("LessSensitive", Keys.OemOpenBrackets);
        moreSensitive = Button("MoreSensitive", Keys.OemCloseBrackets);
        freeCamera = Button("FreeCamera", Keys.F1);
        follow = Button("Follow", Keys.F3);
        followAlt = Button("FollowAlt", Keys.F4);
        followLocked = Button("FollowLocked", Keys.F5);
        lookBack = Button("LookBack", Keys.C);
        wireframe = Button("Wireframe", Keys.F11);
        screenshot = Button("Screenshot", Keys.F12);
        EntityWorld.Satellites.Get<InputActions>().Add(CameraActions);
    }

    /// <summary>The camera's actions - moving, turning, following, the speed keys - to rebind or switch off together.</summary>
    public InputActionMap CameraActions { get; }

    public Boolean InstrumentsEnabled { get; set; }

    public override void UnloadContent()
    {
        //audioManager.Dispose();
    }

    public override bool IsUpdateService => true;
    public override bool IsRenderingService => false;
    public override EntityServiceType ServiceType => EntityServiceType.Update;

    /// <summary>Whether the third-person keys follow the whole object or the exact entity picked. An editor turns it off
    /// and decides for itself.</summary>
    public bool FollowsWholeObject { get; set; } = true;

    /// <summary>The entity the application put the player in charge of - what the third-person keys follow when nothing
    /// is selected.</summary>
    public Entity UserControlledEntity { get; set; }

    /// <summary>
    /// The mouse turns and zooms the camera of the output under the pointer - with the middle button down, a free one is
    /// dragged across its screen plane with the pointer, as far as the plane it looks at moves - the keys and gamepad drive the camera of
    /// the output taking the keyboard - which may be another output, or none. Every visible camera is updated either way.
    /// </summary>
    public override void Update(AppTime appTime)
    {
        userControlledEntity = selection.Current ?? UserControlledEntity;

        if (observatory.PointerOutput is { Camera: { } pointerCamera } && observatory.Controls(pointerCamera))
        {
            HandlePointer(pointerCamera);
        }
        else
        {
            panning = false;
        }

        if (observatory.KeyboardOutput is { Camera: { } keyboardCamera } keyboardOutput)
        {
            if (observatory.Controls(keyboardCamera))
            {
                HandleKeyboard(keyboardOutput, keyboardCamera, appTime);
            }

            HandleOutputKeys();
        }

        var cameras = observatory.CurrentCameras;
        for (int i = 0; i < cameras.Count; i++)
        {
            cameras[i].Update(appTime);
            TakeOffWhenFree(cameras[i]);
        }

        UpdateIdleCameras(appTime);
    }

    private void UpdateIdleCameras(AppTime appTime)
    {
        var cameras = EntityWorld.EntityManager.Cameras;
        for (int i = 0; i < cameras.Count; i++)
        {
            if (cameras[i].GetComponent<Camera>() is { } camera && !Contains(observatory.CurrentCameras, camera))
            {
                camera.Update(appTime);
            }
        }
    }

    private static bool Contains(IReadOnlyList<Camera> cameras, Camera camera)
    {
        for (int i = 0; i < cameras.Count; i++)
        {
            if (cameras[i] == camera)
            {
                return true;
            }
        }

        return false;
    }

    // A camera free again - its travel over, its subject gone, the user's choice - comes off its subject where it stands;
    // a first-person camera rides on its host for good.
    private void TakeOffWhenFree(CameraBase camera)
    {
        var entity = camera.Owner;
        if (entity?.Owner == null || camera.Type.IsThirdPerson() || camera.Type == CameraType.FirstPerson) return;

        entity.Transform.Position = entity.Transform.WorldPosition;
        EntityWorld.EntityManager.RemoveOwner(entity);
        EntityWorld.EntityManager.AddEntity(entity);
    }

    private void HandlePointer(Camera currentCamera)
    {
        if (look.IsDown)
        {
            currentCamera.RotateRelativeXY(
                look.Vector.Y * currentCamera.MouseSensitivity * LookPerPixel,
                -look.Vector.X * currentCamera.MouseSensitivity * LookPerPixel);
        }

        if (zoom.Value != 0)
        {
            currentCamera.Zoom(zoom.Value);
        }

        if (twist.Value != 0)
        {
            currentCamera.RotateForward(-twist.Value);
        }

        Pan(currentCamera);
    }

    private void Pan(Camera camera)
    {
        if (observatory.PointerInput is not { CanLocatePointer: true } input || !input.IsMouseButtonDown(MouseButton.Middle))
        {
            panning = false;
            return;
        }

        var at = input.RelativePosition;
        if (panning && camera == panCamera)
        {
            var moved = at - panFrom;
            var grabbed = camera.WorldPosition + camera.Forward * panDepth;
            var unitsPerPixel = ScreenSpace.UnitsPerPixel(camera, grabbed);
            camera.TranslateRight(-moved.X * unitsPerPixel);
            camera.TranslateUp(-moved.Y * unitsPerPixel);
        }
        else
        {
            panDepth = DepthUnder(camera, at);
        }

        panFrom = at;
        panCamera = camera;
        panning = true;
    }

    private double DepthUnder(Camera camera, Vector2F pixel)
    {
        var hit = Picking.Pick(EntityWorld.RootEntities, PickRay.FromCamera(camera, pixel), PickMode.Triangles);
        if (hit.IsHit)
        {
            var depth = Vector3F.Dot(hit.Point, (Vector3F)camera.Forward);
            if (depth > camera.ZNear)
            {
                return depth;
            }
        }

        return camera.FocusDistance;
    }

    private void HandleKeyboard(UniverseOutput output, Camera currentCamera, AppTime appTime)
    {
        Double cameraMovementSpeed = currentCamera.Velocity * appTime.FrameTime;
        float rotationAngle = currentCamera.RotationSpeed * (float)appTime.FrameTime;

        if (slowerTurn.IsPressed)
        {
            currentCamera.RotationSpeed -= 1f;
        }

        if (fasterTurn.IsPressed)
        {
            currentCamera.RotationSpeed += 1f;
        }

        if (faster.IsPressed)
        {
            currentCamera.Velocity *= 2;
        }

        if (slower.IsPressed)
        {
            currentCamera.Velocity /= 2;
        }

        if (fasterDrag.IsPressed)
        {
            currentCamera.DragVelocity *= 2;
        }

        if (slowerDrag.IsPressed)
        {
            currentCamera.DragVelocity /= 2;
        }

        if (lessSensitive.IsPressed)
        {
            currentCamera.MouseSensitivity -= 0.1f;
        }

        if (moreSensitive.IsPressed)
        {
            currentCamera.MouseSensitivity += 0.1f;
        }

        if (turn.Vector.X != 0)
        {
            currentCamera.RotateUp(-rotationAngle * turn.Vector.X);
        }

        if (turn.Vector.Y != 0)
        {
            currentCamera.RotateRight(-rotationAngle * turn.Vector.Y);
        }

        if (roll.Value != 0)
        {
            currentCamera.RotateForward(rotationAngle * roll.Value);
        }

        if (move.Vector.Y != 0)
        {
            if (currentCamera.Type.IsThirdPerson())
            {
                currentCamera.Zoom(move.Vector.Y * ZoomNotchesPerMeter * cameraMovementSpeed);
            }
            else
            {
                currentCamera.TranslateForward(move.Vector.Y * cameraMovementSpeed);
            }
        }

        if (move.Vector.X != 0)
        {
            currentCamera.TranslateRight(move.Vector.X * cameraMovementSpeed);
        }

        if (rise.Value != 0)
        {
            currentCamera.TranslateUp(rise.Value * cameraMovementSpeed);
        }

        if (freeCamera.IsPressed)
        {
            if (currentCamera.Type != CameraType.Free)
            {
                currentCamera.SetFreeCamera();
            }
        }

        if (follow.IsPressed)
        {
            if (selection.Current?.GetComponent<Camera>() is { } selectedCamera)
            {
                output.Camera = selectedCamera;
                selection.Current = null;
            }
            else
            {
                Follow(currentCamera, userControlledEntity, Vector3F.Zero, CameraType.ThirdPersonFree);
            }
        }

        if (followAlt.IsPressed)
        {
            Follow(currentCamera, userControlledEntity, new Vector3F(-10, 0, 0), CameraType.ThirdPersonFreeAlt);
        }

        if (followLocked.IsPressed)
        {
            Follow(currentCamera, userControlledEntity, new Vector3F(-10, 0, 0), CameraType.ThirdPersonLocked);
        }

        if (lookBack.IsPressed)
        {
            if ((currentCamera.Type == CameraType.ThirdPersonFree) ||
                (currentCamera.Type == CameraType.ThirdPersonFreeAlt))
            {
                currentCamera.SetThirdPersonLookBackwards(true);
            }
        }

        if (lookBack.IsReleased)
        {
            if ((currentCamera.Type == CameraType.ThirdPersonFree) ||
                (currentCamera.Type == CameraType.ThirdPersonFreeAlt))
            {
                currentCamera.SetThirdPersonLookBackwards(false);
            }
        }

    }

    // Keys that do not move the camera, so they work whatever camera the output shows.
    private void HandleOutputKeys()
    {
        if (wireframe.IsPressed)
        {
            selection.Current?.SetWireFrame();
        }

        if (screenshot.IsPressed)
        {
            foreach (var window in observatory.Outputs)
            {
                var filename = $"Screenshot_{window.Name}" + DateTime.Now.ToString("dd_MM_yyyy hh_mm_ss_ffff", CultureInfo.InvariantCulture) + ".jpg";
                window?.TakeScreenshotAsync(filename, ImageFileType.Png);
            }
        }
    }

    private InputAction Button(string name, Keys key)
    {
        return CameraActions.Add(name, InputActionType.Button, InputBinding.Control(Key(key)));
    }

    private static InputControl Key(Keys key)
    {
        return InputControl.Key(key);
    }

    private static InputControl Pad(GamepadAxis axis)
    {
        return InputControl.Gamepad(axis);
    }

    private void Follow(Camera camera, Entity subject, Vector3F relativeRotation, CameraType type)
    {
        if (subject == null)
        {
            return;
        }

        if (FollowsWholeObject)
        {
            subject = RootOf(subject);
        }

        if (camera.Type == type && camera.Subject == subject)
        {
            return;
        }

        EntityWorld.EntityManager.SetOwner(camera.Owner, subject);
        camera.SetThirdPersonCamera(subject, relativeRotation, type);
    }

    private static Entity RootOf(Entity entity)
    {
        var root = entity;
        while (root.Owner != null)
        {
            root = root.Owner;
        }

        return root;
    }

    private void EntityManagerEntityRemoved(object sender, EntityEventArgs e)
    {
        var outputs = observatory.Outputs;
        for (int i = 0; i < outputs.Count; i++)
        {
            var cameras = outputs[i].Cameras;
            for (int j = 0; j < cameras.Count; j++)
            {
                if (e.Entity == cameras[j].Owner)
                {
                    cameras[j].Type = CameraType.Free;
                }
            }
        }
    }
}
