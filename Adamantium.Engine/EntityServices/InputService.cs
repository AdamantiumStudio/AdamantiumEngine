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
    // What is selected in the scene the keys work in: of the output taking the keyboard.
    private Selection Selection => (observatory.KeyboardScene ?? EntityWorld.MainScene).Selection;
    private Observatory observatory;
    private readonly InputAction move;
    private readonly InputAction rise;
    private readonly InputAction turn;
    private readonly InputAction roll;
    private readonly InputAction look;
    private readonly InputAction orbit;
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
    private bool orbiting;
    private Camera orbitCamera;
    private Vector3<double> orbitPivot;
    private const double SteeringQuietSeconds = 0.3;
    private Camera _steered;
    private EditedState _steeredBefore;
    private double _steeringQuietFor;

    public InputService(EntityWorld world) : base(world)
    {
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
        // The same drag with Alt held: about the point under the pointer instead of about the camera itself.
        orbit = CameraActions.Add("Orbit", InputActionType.Vector,
            InputBinding.Stick(InputControl.Mouse(MouseAxis.DeltaX), InputControl.Mouse(MouseAxis.DeltaY),
                modifiers: [InputControl.Mouse(MouseButton.Right), Key(Keys.Alt)]));
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

    /// <summary>Raised on the world's thread when the mouse or the keys have steered a camera of a scene - one an output
    /// looks through, not an output's own - from the press to the release, with how it stood before and after. The
    /// wheel, which has no release, ends its gesture once it has been still for a moment.</summary>
    public event EventHandler<EntityEditEventArgs> CameraSteered;

    /// <summary>Raised on the world's thread when the follow keys have hung a camera on an entity, or the free-camera key
    /// has set it free, with how it was before.</summary>
    public event EventHandler<CameraHungEventArgs> CameraHung;

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
        userControlledEntity = Selection.Current ?? UserControlledEntity;
        FollowSteering(appTime);

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

    // The cameras of the scenes shown that no output looks through - one travelling to its subject, say.
    private void FollowSteering(AppTime appTime)
    {
        var steered = SteeredSceneCamera();
        if (_steered != null)
        {
            if (steered == _steered)
            {
                _steeringQuietFor = 0;
                return;
            }

            _steeringQuietFor += appTime.FrameTime;
            if (steered == null && _steeringQuietFor < SteeringQuietSeconds)
            {
                return;
            }

            EndSteering();
        }

        if (steered?.Owner is { } entity)
        {
            _steered = steered;
            _steeredBefore = EditedState.Of(entity);
            _steeringQuietFor = 0;
        }
    }

    private void EndSteering()
    {
        var camera = _steered;
        _steered = null;
        if (camera.Owner is not { } entity)
        {
            return;
        }

        var after = EditedState.Of(entity);
        if (after != _steeredBefore)
        {
            CameraSteered?.Invoke(this,
                new EntityEditEventArgs(new EntityEdit([new EditedEntity(entity, _steeredBefore, after)])));
        }
    }

    private Camera SteeredSceneCamera()
    {
        if (observatory.PointerOutput is { Camera: { } pointerCamera } && observatory.Controls(pointerCamera)
            && IsSceneCamera(pointerCamera)
            && (look.IsDown || orbit.IsDown || zoom.Value != 0 || twist.Value != 0
                || observatory.PointerInput is { CanLocatePointer: true } input && input.IsMouseButtonDown(MouseButton.Middle)))
        {
            return pointerCamera;
        }

        if (observatory.KeyboardOutput is { Camera: { } keyboardCamera } && observatory.Controls(keyboardCamera)
            && IsSceneCamera(keyboardCamera)
            && (move.Vector.X != 0 || move.Vector.Y != 0 || turn.Vector.X != 0 || turn.Vector.Y != 0 || roll.Value != 0
                || rise.Value != 0))
        {
            return keyboardCamera;
        }

        return null;
    }

    private bool IsSceneCamera(Camera camera)
    {
        if (camera.Owner?.Scene == null)
        {
            return false;
        }

        var outputs = observatory.Outputs;
        for (int i = 0; i < outputs.Count; i++)
        {
            if (outputs[i].OwnCamera == camera)
            {
                return false;
            }
        }

        return true;
    }

    private void UpdateIdleCameras(AppTime appTime)
    {
        foreach (var scene in observatory.ShownScenes)
        {
            var cameras = scene.Cameras;
            for (int i = 0; i < cameras.Count; i++)
            {
                if (cameras[i].GetComponent<Camera>() is { } camera && !Contains(observatory.CurrentCameras, camera))
                {
                    camera.Update(appTime);
                }
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

        var scene = entity.Scene;
        entity.Transform.Position = entity.Transform.WorldPosition;
        EntityWorld.EntityManager.RemoveOwner(entity);
        EntityWorld.EntityManager.AddEntity(entity, scene);
    }

    private void HandlePointer(Camera currentCamera)
    {
        // Both are the right button's drag, and actions of one map do not take a control from each other: while the
        // orbit holds the drag, the look leaves the camera alone.
        if (!Orbit(currentCamera) && look.IsDown)
        {
            currentCamera.RotateRelativeXY(
                look.Vector.Y * currentCamera.MouseSensitivity * LookPerPixel,
                -look.Vector.X * currentCamera.MouseSensitivity * LookPerPixel);
        }

        // The wheel zooms toward what is under the pointer, as a canvas does - where the wheel was turned, when the host
        // says; with no pointer to go by, the middle.
        if (zoom.Value != 0)
        {
            if (observatory.PointerInput is { CanLocatePointer: true } input)
            {
                var pixel = input.WheelPosition ?? input.RelativePosition;
                currentCamera.ZoomAt(zoom.Value, pixel, DepthUnder(currentCamera, pixel));
            }
            else
            {
                currentCamera.Zoom(zoom.Value);
            }
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

    // Alt with the right button turns a free camera about a point: what was under the pointer when the button went
    // down, or the middle of the view where nothing was - out in space, say. The point stays until the button is let go.
    // True while the orbit holds the drag.
    private bool Orbit(Camera camera)
    {
        if (observatory.PointerInput is not { CanLocatePointer: true } input || !input.IsMouseButtonDown(MouseButton.Right)
            || (!orbit.IsDown && !(orbiting && camera == orbitCamera)))
        {
            orbiting = false;
            return false;
        }

        if (!orbiting || camera != orbitCamera)
        {
            orbitPivot = PointUnder(camera, input.RelativePosition);
            orbitCamera = camera;
            orbiting = true;
        }

        if (orbit.IsDown)
        {
            camera.Orbit(orbitPivot,
                orbit.Vector.Y * camera.MouseSensitivity * LookPerPixel,
                -orbit.Vector.X * camera.MouseSensitivity * LookPerPixel,
                observatory.PointerOutput?.OrbitStyle ?? CameraOrbitStyle.Turntable);
        }

        return true;
    }

    private double DepthUnder(Camera camera, Vector2F pixel) =>
        HitUnder(camera, pixel, out var fromCamera)
            ? Vector3F.Dot(fromCamera, camera.Forward.As<float>())
            : camera.FocusDistance;

    // The point of the scene under the pixel, in the world; the middle of the view where nothing is.
    private Vector3<double> PointUnder(Camera camera, Vector2F pixel) =>
        HitUnder(camera, pixel, out var fromCamera)
            ? camera.WorldPosition + fromCamera.As<double>()
            : camera.WorldPosition + camera.Forward * camera.FocusDistance;

    // Whether a mesh of the scene shown is under the pixel, ahead of the camera's near plane; where, from the camera.
    private bool HitUnder(Camera camera, Vector2F pixel, out Vector3F fromCamera)
    {
        var roots = (observatory.PointerScene ?? EntityWorld.MainScene).RootEntities;
        var hit = Picking.Pick(roots, PickRay.FromCamera(camera, pixel), PickMode.Triangles);
        fromCamera = hit.Point;
        return hit.IsHit && Vector3F.Dot(hit.Point, camera.Forward.As<float>()) > camera.ZNear;
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

        var typeBefore = currentCamera.Type;
        var hostBefore = currentCamera.Owner?.Owner;
        var stateBefore = currentCamera.Owner is { } cameraEntity ? EditedState.Of(cameraEntity) : default;
        if (freeCamera.IsPressed)
        {
            if (currentCamera.Type != CameraType.Free)
            {
                currentCamera.SetFreeCamera();
            }
        }

        if (follow.IsPressed)
        {
            if (Selection.Current?.GetComponent<Camera>() is { } selectedCamera)
            {
                output.Camera = selectedCamera;
                Selection.Current = null;
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

        var hostAfter = currentCamera.Owner?.Owner;
        if (currentCamera.Owner is { } hung && (currentCamera.Type != typeBefore || hostAfter != hostBefore))
        {
            CameraHung?.Invoke(this,
                new CameraHungEventArgs(hung, typeBefore, hostBefore, stateBefore, currentCamera.Type, hostAfter));
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
            Selection.Current?.SetWireFrame();
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
                if (IsUnder(cameras[j].Owner, e.Entity))
                {
                    cameras[j].Type = CameraType.Free;
                }
            }
        }

        if (IsUnder(UserControlledEntity, e.Entity))
        {
            UserControlledEntity = null;
        }
    }

    private static bool IsUnder(Entity entity, Entity ancestor)
    {
        for (var current = entity; current != null; current = current.Owner)
        {
            if (current == ancestor)
            {
                return true;
            }
        }

        return false;
    }
}
