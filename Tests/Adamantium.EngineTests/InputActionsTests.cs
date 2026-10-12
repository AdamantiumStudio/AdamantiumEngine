using System;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class InputActionsTests
{
    private TestOutput keyboard;
    private TestOutput pointer;
    private FakeGamepadBackend gamepads;
    private InputActions actions;

    [SetUp]
    public void Build()
    {
        gamepads = new FakeGamepadBackend();
        keyboard = new TestOutput(gamepads);
        pointer = new TestOutput();
        actions = new InputActions();
    }

    [Test]
    public void AButton_IsPressedOnce_HeldWhileDown_AndReleasedOnce()
    {
        var jump = Map().Add("Jump", InputActionType.Button, InputBinding.Control(InputControl.Key(Keys.Space)));

        keyboard.Key(Keys.Space, InputType.Down);
        Frame();
        Assert.That(jump.IsPressed && jump.IsDown, Is.True);
        Assert.That(jump.Value, Is.EqualTo(1f));

        Frame();
        Assert.That(jump.IsPressed, Is.False);
        Assert.That(jump.IsDown, Is.True);

        keyboard.Key(Keys.Space, InputType.Up);
        Frame();
        Assert.That(jump.IsReleased, Is.True);
        Assert.That(jump.IsDown, Is.False);
    }

    [Test]
    public void ATapShorterThanAFrame_IsStillPressedAndReleased()
    {
        var jump = Map().Add("Jump", InputActionType.Button, InputBinding.Control(InputControl.Key(Keys.Space)));

        keyboard.Key(Keys.Space, InputType.Down);
        keyboard.Key(Keys.Space, InputType.Up);
        Frame();

        Assert.That(jump.IsPressed, Is.True);
        Assert.That(jump.IsReleased, Is.True);
    }

    [Test]
    public void AnAxis_IsThePositiveMinusTheNegative()
    {
        var rise = Map().Add("Rise", InputActionType.Axis,
            InputBinding.Axis(InputControl.Key(Keys.E), InputControl.Key(Keys.Q)));

        keyboard.Key(Keys.Q, InputType.Down);
        Frame();
        Assert.That(rise.Value, Is.EqualTo(1f));

        keyboard.Key(Keys.E, InputType.Down);
        Frame();
        Assert.That(rise.Value, Is.EqualTo(0f));
        Assert.That(rise.IsDown, Is.False);
    }

    [Test]
    public void KeysAndAStick_OnOneVector_AddUpToNoMoreThanOne()
    {
        var move = Map().Add("Move", InputActionType.Vector,
            InputBinding.Vector(InputControl.Key(Keys.W), InputControl.Key(Keys.S), InputControl.Key(Keys.A),
                InputControl.Key(Keys.D)),
            InputBinding.Stick(InputControl.Gamepad(GamepadAxis.LeftStickX), InputControl.Gamepad(GamepadAxis.LeftStickY)));
        gamepads.Connected.Add(new FakeGamepad { State = { LeftThumb = new Vector2F(0, 1) } });

        keyboard.Key(Keys.W, InputType.Down);
        keyboard.Key(Keys.D, InputType.Down);
        Frame();

        Assert.That(move.Vector.X, Is.EqualTo(1f));
        Assert.That(move.Vector.Y, Is.EqualTo(1f));
    }

    [Test]
    public void TheMouse_IsReadFromTheOutputUnderThePointer_AndIsNotBounded()
    {
        var look = Map().Add("Look", InputActionType.Vector,
            InputBinding.Stick(InputControl.Mouse(MouseAxis.DeltaX), InputControl.Mouse(MouseAxis.DeltaY),
                modifiers: InputControl.Mouse(MouseButton.Right)));
        var zoom = Map().Add("Zoom", InputActionType.Axis, InputBinding.Control(InputControl.Mouse(MouseAxis.Wheel)));

        keyboard.Button(MouseButton.Right, InputType.Down);
        keyboard.Move(40, -10);
        Frame();
        Assert.That(look.IsDown, Is.False);

        pointer.Button(MouseButton.Right, InputType.Down);
        pointer.Move(40, -10);
        pointer.Wheel(240);
        Frame();
        Assert.That(look.Vector.X, Is.EqualTo(40f));
        Assert.That(look.Vector.Y, Is.EqualTo(-10f));
        Assert.That(zoom.Value, Is.EqualTo(2f));
    }

    // Fingers moved side by side turn the camera as the mouse does, a pinch zooms as the wheel does, and a turn of them is
    // read in degrees - from the output under the fingers, unbounded.
    [Test]
    public void AGesture_IsReadFromTheOutputUnderTheFingers_AndIsNotBounded()
    {
        var look = Map().Add("Look", InputActionType.Vector,
            InputBinding.Stick(InputControl.Gesture(GestureAxis.PanX), InputControl.Gesture(GestureAxis.PanY)));
        var zoom = Map().Add("Zoom", InputActionType.Axis,
            InputBinding.Control(InputControl.Gesture(GestureAxis.Pinch), 4));
        var twist = Map().Add("Twist", InputActionType.Axis, InputBinding.Control(InputControl.Gesture(GestureAxis.Rotate)));

        keyboard.Gesture(new Vector2F(30, 0), 2, 15);
        Frame();
        Assert.That(look.IsDown, Is.False);

        pointer.Gesture(new Vector2F(30, -12), 2, 15);
        pointer.Gesture(new Vector2F(10, 0), 2, 5);
        Frame();
        Assert.Multiple(() =>
        {
            Assert.That(look.Vector.X, Is.EqualTo(40f));
            Assert.That(look.Vector.Y, Is.EqualTo(-12f));
            Assert.That(zoom.Value, Is.EqualTo(8f), "spread twice, each time twice as far apart: two doublings, four notches each");
            Assert.That(twist.Value, Is.EqualTo(20f));
        });

        Frame();
        Assert.That(look.IsDown || zoom.Value != 0 || twist.Value != 0, Is.False, "a frame with no gesture moves nothing");
    }

    [Test]
    public void AModifiedBinding_CountsOnlyWhileTheModifierIsHeld()
    {
        var save = Map().Add("Save", InputActionType.Button,
            InputBinding.Control(InputControl.Key(Keys.S), modifiers: InputControl.Key(Keys.Control)));

        keyboard.Key(Keys.S, InputType.Down);
        Frame();
        Assert.That(save.IsDown, Is.False);

        keyboard.Key(Keys.LeftControl, InputType.Down);
        Frame();
        Assert.That(save.IsPressed, Is.True);
    }

    [Test]
    public void AHigherMap_TakesTheControl_AndSwitchedOffGivesItBack()
    {
        var menu = actions.Add(new InputActionMap("Menu", priority: 10));
        var game = actions.Add(new InputActionMap("Game"));
        var close = menu.Add("Close", InputActionType.Button, InputBinding.Control(InputControl.Key(Keys.Escape)));
        var pause = game.Add("Pause", InputActionType.Button, InputBinding.Control(InputControl.Key(Keys.Escape)));

        keyboard.Key(Keys.Escape, InputType.Down);
        Frame();
        Assert.That(close.IsPressed, Is.True);
        Assert.That(pause.IsDown, Is.False);

        menu.IsEnabled = false;
        Frame();
        Assert.That(close.IsReleased, Is.True);
        Assert.That(pause.IsDown, Is.True);
    }

    [Test]
    public void AnActionHeld_WhenAHigherMapTakesItsControl_IsReleased()
    {
        var menu = actions.Add(new InputActionMap("Menu", priority: 10));
        var game = actions.Add(new InputActionMap("Game"));
        var close = menu.Add("Close", InputActionType.Button, InputBinding.Control(InputControl.Key(Keys.Escape)));
        var pause = game.Add("Pause", InputActionType.Button, InputBinding.Control(InputControl.Key(Keys.Escape)));
        menu.IsEnabled = false;

        keyboard.Key(Keys.Escape, InputType.Down);
        Frame();
        Assert.That(pause.IsDown, Is.True);

        menu.IsEnabled = true;
        Frame();
        Assert.That(close.IsDown, Is.True);
        Assert.That(pause.IsReleased, Is.True);
        Assert.That(pause.IsDown, Is.False);
    }

    [Test]
    public void ATrigger_BoundToAButton_IsDownPastHalfItsTravel()
    {
        var fire = Map().Add("Fire", InputActionType.Button,
            InputBinding.Control(InputControl.Gamepad(GamepadAxis.RightTrigger)));
        var gamepad = new FakeGamepad { State = { RightTrigger = 0.4f } };
        gamepads.Connected.Add(gamepad);

        Frame();
        Assert.That(fire.IsDown, Is.False);

        gamepad.State.RightTrigger = 0.6f;
        Frame();
        Assert.That(fire.IsPressed, Is.True);
    }

    [Test]
    public void Rebindings_RoundTripAsJson_AndReachMapsAddedLater()
    {
        var map = Map();
        var jump = map.Add("Jump", InputActionType.Button, InputBinding.Control(InputControl.Key(Keys.Space)));
        jump.Rebind(
            InputBinding.Control(InputControl.Gamepad(GamepadButton.A)),
            InputBinding.Control(InputControl.Key(Keys.J), modifiers: InputControl.Key(Keys.Shift)));
        var json = actions.SaveOverrides();

        var later = new InputActions();
        later.LoadOverrides(json);
        var fresh = new InputActionMap("Player");
        var freshJump = fresh.Add("Jump", InputActionType.Button, InputBinding.Control(InputControl.Key(Keys.Space)));
        later.Add(fresh);

        Assert.That(freshJump.IsRebound, Is.True);
        Assert.That(freshJump.Bindings, Has.Count.EqualTo(2));
        Assert.That(freshJump.Bindings[0].Controls[0], Is.EqualTo(InputControl.Gamepad(GamepadButton.A)));
        Assert.That(freshJump.Bindings[1].Modifiers[0], Is.EqualTo(InputControl.Key(Keys.Shift)));
    }

    [Test]
    public void AControl_WrittenAsText_ReadsBackTheSame()
    {
        InputControl[] controls =
        [
            InputControl.Key(Keys.W), InputControl.Mouse(MouseButton.Right), InputControl.Mouse(MouseAxis.Wheel),
            InputControl.Gamepad(GamepadButton.Paddle1), InputControl.Gamepad(GamepadAxis.LeftTrigger),
            InputControl.Gesture(GestureAxis.Pinch)
        ];

        foreach (var control in controls)
        {
            Assert.That(InputControl.Parse(control.ToString()), Is.EqualTo(control), control.ToString());
        }

        Assert.That(() => InputControl.Parse("Key:NoSuchKey"), Throws.TypeOf<FormatException>());
        Assert.That(() => InputControl.Parse("W"), Throws.TypeOf<FormatException>());
    }

    private InputActionMap Map()
    {
        if (actions.Maps.Count == 0)
        {
            actions.Add(new InputActionMap("Player"));
        }

        return actions.Maps[0];
    }

    private void Frame()
    {
        keyboard.Frame();
        pointer.Frame();
        actions.Update(keyboard.Input, pointer.Input);
    }
}
