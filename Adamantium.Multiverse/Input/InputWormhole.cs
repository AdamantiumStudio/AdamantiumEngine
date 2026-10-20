using Adamantium.Core;
using Adamantium.Mathematics;

namespace Adamantium.Multiverse.Input;

/// <summary>
/// Input of one <see cref="UniverseOutput"/>: its keyboard and pointer, fed by the output directly, plus the gamepads -
/// which only the active output reports.
/// </summary>
public class InputWormhole
{
    private readonly HashSet<Keys> downKeys = [];
    private readonly HashSet<Keys> pressedKeys = [];
    private readonly HashSet<Keys> releasedKeys = [];
    private readonly ButtonState[] mouseButtons = new ButtonState[5];
    private readonly int[] clickCounts = new int[5];

    private readonly List<string> textInputs = [];

    private readonly UniverseOutput output;
    private readonly GamepadHub gamepads;
    private readonly IUniverse universe;

    private string text = string.Empty;
    private Vector2F absolutePosition;
    private bool isPointerHeld;
    private Vector2F rawMouseDelta;
    private int mouseWheelDelta;
    private Vector2F gesturePan;
    private float gesturePinch;
    private float gestureRotation;

    public InputWormhole(UniverseOutput output, GamepadHub gamepads) : this(output, gamepads, null)
    {
    }

    public InputWormhole(UniverseOutput output, GamepadHub gamepads, IUniverse universe)
    {
        this.output = output;
        this.gamepads = gamepads;
        this.universe = universe;
    }

    internal void OnKeyboardInput(KeyboardInput e)
    {
        lock (KeyboardInputs)
        {
            KeyboardInputs.Add(e);
        }
        RequestFrame();
    }

    internal void OnMouseInput(MouseInput e)
    {
        lock (MouseInputs)
        {
            MouseInputs.Add(e);
        }
        RequestFrame();
    }

    internal void OnGestureInput(GestureInput e)
    {
        lock (GestureInputs)
        {
            GestureInputs.Add(e);
        }
        RequestFrame();
    }

    internal void OnTextInput(string value)
    {
        lock (textInputs)
        {
            textInputs.Add(value);
        }
        RequestFrame();
    }

    internal void RequestFrame() => universe?.RequestFrame();

    internal List<KeyboardInput> KeyboardInputs { get; } = [];

    internal List<MouseInput> MouseInputs { get; } = [];

    internal List<GestureInput> GestureInputs { get; } = [];

    /// <summary>
    /// Whether the output takes the keyboard. Keys are still tracked while it is off, so none sticks when it comes back.
    /// </summary>
    public bool IsKeyboardEnabled { get; set; } = true;

    /// <summary>
    /// Whether keys typed into the output go to the game now: the keyboard is on and the simulation is not paused. A host
    /// that also uses keys for its own interface asks this before keeping one from it.
    /// </summary>
    public bool TakesKeyboard => IsKeyboardEnabled && universe is not { IsSimulationPaused: true };

    /// <summary>
    /// Whether the output takes the pointer: the mouse's buttons, wheel and motion, and gestures of fingers.
    /// </summary>
    public bool IsMouseEnabled { get; set; } = true;

    /// <summary>
    /// Whether the output takes the gamepads. Even when on, only the output receiving the keyboard reports them.
    /// </summary>
    public bool IsGamepadEnabled { get; set; } = true;

    /// <summary>Whether <see cref="RelativePosition"/> can answer at all: while the surface is off screen there is no
    /// point in its coordinates to answer with.</summary>
    public bool CanLocatePointer => output.IsVisible;

    public Vector2F RawMouseDelta => IsMouseEnabled ? rawMouseDelta : Vector2F.Zero;

    /// <summary>
    /// The text typed into the output this frame, in the order it was typed - characters, not keys, so it follows the
    /// keyboard layout and takes what an input method composed. Empty when nothing was typed or the keyboard is off.
    /// </summary>
    public string Text => IsKeyboardEnabled ? text : string.Empty;

    public bool IsKeyDown(Keys key)
    {
        return IsKeyboardEnabled && Holds(downKeys, key);
    }

    public bool IsKeyPressed(Keys key)
    {
        return IsKeyboardEnabled && Holds(pressedKeys, key);
    }

    public bool IsKeyReleased(Keys key)
    {
        return IsKeyboardEnabled && Holds(releasedKeys, key);
    }

    public bool IsMouseButtonDown(MouseButton button)
    {
        if (button == MouseButton.None || !IsMouseEnabled)
        {
            return false;
        }

        return mouseButtons[(int) button].IsDown;
    }

    public bool IsMouseButtonPressed(MouseButton button)
    {
        if (button == MouseButton.None || !IsMouseEnabled)
        {
            return false;
        }

        return mouseButtons[(int) button].IsPressed;
    }

    public bool IsMouseButtonReleased(MouseButton button)
    {
        if (button == MouseButton.None || !IsMouseEnabled)
        {
            return false;
        }

        return mouseButtons[(int)button].IsReleased;
    }

    /// <summary>
    /// How many clicks in a row the press of <paramref name="button"/> this frame makes, 2 for a double click; 0 when it
    /// was not pressed this frame.
    /// </summary>
    public int MouseClickCount(MouseButton button)
    {
        if (button == MouseButton.None || !IsMouseEnabled)
        {
            return 0;
        }

        return clickCounts[(int)button];
    }

    public int MouseWheelDelta => IsMouseEnabled ? mouseWheelDelta : 0;

    /// <summary>How far gestures of fingers moved this frame, in the output's units.</summary>
    public Vector2F GesturePan => IsMouseEnabled ? gesturePan : Vector2F.Zero;

    /// <summary>How much gestures of fingers spread this frame, in doublings: 1 twice as far apart, -1 to half.</summary>
    public float GesturePinch => IsMouseEnabled ? gesturePinch : 0;

    /// <summary>How far gestures of fingers turned this frame, in degrees, clockwise on screen.</summary>
    public float GestureRotation => IsMouseEnabled ? gestureRotation : 0;

    public Vector2F AbsolutePosition => absolutePosition;

    public Vector2F RelativePosition => output.PointToSurface(absolutePosition);

    /// <summary>Holds the pointer for a drag the universe runs - turning a gizmo, say: the cursor is hidden and pinned, and
    /// the motion arrives as <see cref="RawMouseDelta"/>. Released with false, which puts the cursor back.</summary>
    public void HoldPointer(bool hold)
    {
        if (hold == isPointerHeld)
        {
            return;
        }
        isPointerHeld = hold;
        output.HoldPointer(hold, absolutePosition);
    }

    public bool IsGamepadButtonDown(int gamepadIndex, GamepadButton button)
    {
        return ReportsGamepads() && gamepads.IsButtonDown(gamepadIndex, button);
    }

    public bool IsGamepadButtonPressed(int gamepadIndex, GamepadButton button)
    {
        return ReportsGamepads() && gamepads.IsButtonPressed(gamepadIndex, button);
    }

    public bool IsGamepadButtonReleased(int gamepadIndex, GamepadButton button)
    {
        return ReportsGamepads() && gamepads.IsButtonReleased(gamepadIndex, button);
    }

    /// <summary>State of a gamepad - empty unless this output receives the keyboard and takes gamepads.</summary>
    public GamepadState GetGamepadState(int gamepadIndex)
    {
        return ReportsGamepads() ? gamepads.GetState(gamepadIndex) : default;
    }

    private bool ReportsGamepads()
    {
        return IsGamepadEnabled && output.IsKeyboardFocused;
    }

    public void Update(AppTime time)
    {
        UpdateKeyboard();
        UpdateText();
        UpdateMouse();
        UpdateGestures();
    }

    private void UpdateGestures()
    {
        gesturePan = Vector2F.Zero;
        gesturePinch = 0;
        gestureRotation = 0;

        lock (GestureInputs)
        {
            foreach (var gesture in GestureInputs)
            {
                gesturePan += gesture.Translation;
                if (gesture.Scale > 0)
                {
                    gesturePinch += MathF.Log2(gesture.Scale);
                }

                gestureRotation += gesture.Rotation;
            }

            GestureInputs.Clear();
        }
    }

    private void UpdateText()
    {
        lock (textInputs)
        {
            text = textInputs.Count switch
            {
                0 => string.Empty,
                1 => textInputs[0],
                _ => string.Concat(textInputs)
            };
            textInputs.Clear();
        }
    }

    private void UpdateKeyboard()
    {
        pressedKeys.Clear();
        releasedKeys.Clear();

        lock (KeyboardInputs)
        {
            foreach (var keyboardInput in KeyboardInputs)
            {
                switch (keyboardInput.InputType)
                {
                    case InputType.Up:
                        if (downKeys.Remove(keyboardInput.Key))
                        {
                            releasedKeys.Add(keyboardInput.Key);
                        }
                        break;

                    case InputType.Down:
                        if (downKeys.Add(keyboardInput.Key))
                        {
                            pressedKeys.Add(keyboardInput.Key);
                        }
                        break;
                }
            }
            KeyboardInputs.Clear();
        }

        if (!output.IsKeyboardFocused)
        {
            releasedKeys.UnionWith(downKeys);
            downKeys.Clear();
        }
    }

    private void UpdateMouse()
    {
        absolutePosition = output.PointerScreenPosition;
        mouseWheelDelta = 0;
        rawMouseDelta = Vector2F.Zero;

        for (int i = 0; i < mouseButtons.Length; ++i)
        {
            mouseButtons[i].Reset();
            clickCounts[i] = 0;
        }

        lock (MouseInputs)
        {
            foreach (var mouseInput in MouseInputs)
            {
                switch (mouseInput.InputType)
                {
                    case InputType.Up when IsTracked(mouseInput.Button):
                        Release(ref mouseButtons[(int)mouseInput.Button]);
                        break;
                    case InputType.Down when IsTracked(mouseInput.Button):
                        Press(ref mouseButtons[(int)mouseInput.Button]);
                        clickCounts[(int)mouseInput.Button] = mouseInput.ClickCount;
                        break;
                    case InputType.Wheel:
                        mouseWheelDelta += mouseInput.WheelDelta;
                        break;
                    case InputType.RawDelta:
                        rawMouseDelta += mouseInput.Delta;
                        break;
                }
            }
            MouseInputs.Clear();
        }

        if (!output.IsPointerOver && !isPointerHeld)
        {
            for (int i = 0; i < mouseButtons.Length; ++i)
            {
                if (mouseButtons[i].IsDown)
                {
                    Release(ref mouseButtons[i]);
                }
            }
        }
    }

    private static bool Holds(HashSet<Keys> keys, Keys key)
    {
        return key switch
        {
            Keys.Shift => keys.Contains(Keys.Shift) || keys.Contains(Keys.LeftShift) || keys.Contains(Keys.RightShift),
            Keys.Control => keys.Contains(Keys.Control) || keys.Contains(Keys.LeftControl) ||
                            keys.Contains(Keys.RightControl),
            Keys.Alt => keys.Contains(Keys.Alt) || keys.Contains(Keys.LeftAlt) || keys.Contains(Keys.RightAlt),
            _ => keys.Contains(key)
        };
    }

    private bool IsTracked(MouseButton button)
    {
        return button >= 0 && (int)button < mouseButtons.Length;
    }

    private static void Press(ref ButtonState state)
    {
        if (!state.IsDown)
        {
            state.IsPressed = true;
        }
        state.IsDown = true;
        state.IsReleased = false;
    }

    private static void Release(ref ButtonState state)
    {
        state.IsDown = false;
        state.IsReleased = true;
    }
}
