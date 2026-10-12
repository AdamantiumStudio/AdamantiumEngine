namespace Adamantium.Multiverse.Input;

internal readonly struct InputReader
{
    private const float WheelNotch = 120f;

    private readonly InputWormhole keyboard;
    private readonly InputWormhole pointer;
    private readonly int gamepadSlot;

    public InputReader(InputWormhole keyboard, InputWormhole pointer, int gamepadSlot)
    {
        this.keyboard = keyboard;
        this.pointer = pointer;
        this.gamepadSlot = gamepadSlot;
    }

    public float Value(InputControl control)
    {
        switch (control.Kind)
        {
            case InputControlKind.Key:
                return keyboard != null && keyboard.IsKeyDown((Keys)control.Code) ? 1 : 0;
            case InputControlKind.MouseButton:
                return pointer != null && pointer.IsMouseButtonDown((MouseButton)control.Code) ? 1 : 0;
            case InputControlKind.MouseAxis:
                return ReadMouseAxis((MouseAxis)control.Code);
            case InputControlKind.GamepadButton:
                return keyboard != null && keyboard.IsGamepadButtonDown(gamepadSlot, (GamepadButton)control.Code) ? 1 : 0;
            case InputControlKind.GamepadAxis:
                return ReadGamepadAxis((GamepadAxis)control.Code);
            case InputControlKind.GestureAxis:
                return ReadGestureAxis((GestureAxis)control.Code);
            default:
                return 0;
        }
    }

    private float ReadGestureAxis(GestureAxis axis)
    {
        if (pointer == null)
        {
            return 0;
        }

        return axis switch
        {
            GestureAxis.PanX => pointer.GesturePan.X,
            GestureAxis.PanY => pointer.GesturePan.Y,
            GestureAxis.Pinch => pointer.GesturePinch,
            GestureAxis.Rotate => pointer.GestureRotation,
            _ => 0
        };
    }

    public bool Pressed(InputControl control)
    {
        return control.Kind switch
        {
            InputControlKind.Key => keyboard != null && keyboard.IsKeyPressed((Keys)control.Code),
            InputControlKind.MouseButton => pointer != null && pointer.IsMouseButtonPressed((MouseButton)control.Code),
            InputControlKind.GamepadButton => keyboard != null &&
                                              keyboard.IsGamepadButtonPressed(gamepadSlot, (GamepadButton)control.Code),
            _ => false
        };
    }

    public bool Released(InputControl control)
    {
        return control.Kind switch
        {
            InputControlKind.Key => keyboard != null && keyboard.IsKeyReleased((Keys)control.Code),
            InputControlKind.MouseButton => pointer != null && pointer.IsMouseButtonReleased((MouseButton)control.Code),
            InputControlKind.GamepadButton => keyboard != null &&
                                              keyboard.IsGamepadButtonReleased(gamepadSlot, (GamepadButton)control.Code),
            _ => false
        };
    }

    private float ReadMouseAxis(MouseAxis axis)
    {
        if (pointer == null)
        {
            return 0;
        }

        return axis switch
        {
            MouseAxis.DeltaX => pointer.RawMouseDelta.X,
            MouseAxis.DeltaY => pointer.RawMouseDelta.Y,
            MouseAxis.Wheel => pointer.MouseWheelDelta / WheelNotch,
            _ => 0
        };
    }

    private float ReadGamepadAxis(GamepadAxis axis)
    {
        if (keyboard == null)
        {
            return 0;
        }

        var state = keyboard.GetGamepadState(gamepadSlot);
        return axis switch
        {
            GamepadAxis.LeftStickX => state.LeftThumb.X,
            GamepadAxis.LeftStickY => state.LeftThumb.Y,
            GamepadAxis.RightStickX => state.RightThumb.X,
            GamepadAxis.RightStickY => state.RightThumb.Y,
            GamepadAxis.LeftTrigger => state.LeftTrigger,
            GamepadAxis.RightTrigger => state.RightTrigger,
            _ => 0
        };
    }
}
