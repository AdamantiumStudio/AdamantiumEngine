using Adamantium.Mathematics;
using Adamantium.Multiverse.Input;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class InputWormholeTests
{
    [Test]
    public void AClickWithinOneFrame_IsPressedAndReleased()
    {
        var output = new TestOutput();

        output.Button(MouseButton.Left, InputType.Down);
        output.Button(MouseButton.Left, InputType.Up);
        output.Frame();

        Assert.That(output.Input.IsMouseButtonPressed(MouseButton.Left), Is.True);
        Assert.That(output.Input.IsMouseButtonReleased(MouseButton.Left), Is.True);
        Assert.That(output.Input.IsMouseButtonDown(MouseButton.Left), Is.False);
    }

    [Test]
    public void GesturesOfAFrame_AddUp_AndAreGoneTheNextFrame()
    {
        var output = new TestOutput();

        output.Gesture(new Vector2F(5, 1), 2, 10);
        output.Gesture(new Vector2F(3, 2), 0.5f, -4);
        output.Gesture(new Vector2F(0, 0), 4, 0);
        output.Frame();

        Assert.Multiple(() =>
        {
            Assert.That(output.Input.GesturePan, Is.EqualTo(new Vector2F(8, 3)));
            Assert.That(output.Input.GesturePinch, Is.EqualTo(2f), "x2, x0.5, x4: two doublings");
            Assert.That(output.Input.GestureRotation, Is.EqualTo(6f));
        });

        output.Frame();
        Assert.That((output.Input.GesturePan, output.Input.GesturePinch, output.Input.GestureRotation),
            Is.EqualTo((Vector2F.Zero, 0f, 0f)));
    }

    [Test]
    public void Gestures_AreNotTaken_WhileThePointerIsOff()
    {
        var output = new TestOutput();
        output.Input.IsMouseEnabled = false;

        output.Gesture(new Vector2F(5, 1), 2, 10);
        output.Frame();

        Assert.That((output.Input.GesturePan, output.Input.GesturePinch, output.Input.GestureRotation),
            Is.EqualTo((Vector2F.Zero, 0f, 0f)));
    }

    [TestCase(false, true, ExpectedResult = true)]
    [TestCase(true, true, ExpectedResult = false)]
    [TestCase(false, false, ExpectedResult = false)]
    [TestCase(true, false, ExpectedResult = false)]
    [TestCase(null, true, ExpectedResult = true)]
    public bool TheKeyboard_IsTakenWhileOnAndTheSimulationRuns(bool? paused, bool keyboardEnabled)
    {
        var universe = paused == null ? null : new TestUniverse { IsSimulationPaused = paused.Value };
        var input = new InputWormhole(new TestOutput(), new GamepadHub(new NoGamepadBackend(), null), universe)
        {
            IsKeyboardEnabled = keyboardEnabled
        };

        return input.TakesKeyboard;
    }

    [Test]
    public void AKeyTappedWithinOneFrame_IsPressedAndReleased()
    {
        var output = new TestOutput();

        output.Key(Keys.W, InputType.Down);
        output.Key(Keys.W, InputType.Up);
        output.Frame();

        Assert.That(output.Input.IsKeyPressed(Keys.W), Is.True);
        Assert.That(output.Input.IsKeyReleased(Keys.W), Is.True);
        Assert.That(output.Input.IsKeyDown(Keys.W), Is.False);
    }

    [Test]
    public void AKeyHeld_WhenTheOutputLosesTheKeyboard_IsReleasedOnce()
    {
        var output = new TestOutput();
        output.Key(Keys.W, InputType.Down);
        output.Frame();

        output.KeyboardFocused = false;
        output.Frame();

        Assert.That(output.Input.IsKeyDown(Keys.W), Is.False);
        Assert.That(output.Input.IsKeyReleased(Keys.W), Is.True);

        output.Frame();

        Assert.That(output.Input.IsKeyReleased(Keys.W), Is.False);
    }

    [Test]
    public void AButtonHeld_WhenThePointerLeavesTheOutput_IsReleased()
    {
        var output = new TestOutput();
        output.Button(MouseButton.Left, InputType.Down);
        output.Frame();

        output.PointerOver = false;
        output.Frame();

        Assert.That(output.Input.IsMouseButtonDown(MouseButton.Left), Is.False);
        Assert.That(output.Input.IsMouseButtonReleased(MouseButton.Left), Is.True);
    }

    [Test]
    public void AButtonHeld_WhileTheOutputHoldsThePointer_StaysDownWherever()
    {
        var output = new TestOutput();
        output.Button(MouseButton.Left, InputType.Down);
        output.Frame();
        output.Input.HoldPointer(true);

        output.PointerOver = false;
        output.Frame();

        Assert.That(output.Input.IsMouseButtonDown(MouseButton.Left), Is.True);
    }

    [Test]
    public void APressWithoutAButton_IsIgnored()
    {
        var output = new TestOutput();

        output.Button(MouseButton.None, InputType.Down);

        Assert.DoesNotThrow(() => output.Frame());
    }

    [Test]
    public void AKeyReleasedWhileTheKeyboardIsOff_IsNotDownWhenItComesBack()
    {
        var output = new TestOutput();
        output.Key(Keys.W, InputType.Down);
        output.Frame();

        output.Input.IsKeyboardEnabled = false;
        output.Key(Keys.W, InputType.Up);
        output.Frame();
        output.Input.IsKeyboardEnabled = true;

        Assert.That(output.Input.IsKeyDown(Keys.W), Is.False);
    }

    [Test]
    public void EitherShift_IsShift()
    {
        var output = new TestOutput();

        output.Key(Keys.RightShift, InputType.Down);
        output.Frame();

        Assert.That(output.Input.IsKeyDown(Keys.Shift), Is.True);
        Assert.That(output.Input.IsKeyPressed(Keys.Shift), Is.True);
        Assert.That(output.Input.IsKeyDown(Keys.Control), Is.False);

        output.Key(Keys.RightShift, InputType.Up);
        output.Frame();

        Assert.That(output.Input.IsKeyDown(Keys.Shift), Is.False);
        Assert.That(output.Input.IsKeyReleased(Keys.Shift), Is.True);
    }

    [Test]
    public void KeyValues_AreUsbHidUsages()
    {
        Assert.That((uint)Keys.A, Is.EqualTo(0x0007_0004u));
        Assert.That((uint)Keys.W, Is.EqualTo(0x0007_001Au));
        Assert.That((uint)Keys.Enter, Is.EqualTo(0x0007_0028u));
        Assert.That((uint)Keys.NumPadEnter, Is.EqualTo(0x0007_0058u));
        Assert.That((uint)Keys.LeftShift, Is.EqualTo(0x0007_00E1u));
        Assert.That((uint)Keys.VolumeUp, Is.EqualTo(0x000C_00E9u));
    }

    [Test]
    public void TypedText_ArrivesInOrderForOneFrame()
    {
        var output = new TestOutput();

        output.Type("П");
        output.Type("ри");
        output.Frame();
        Assert.That(output.Input.Text, Is.EqualTo("При"));

        output.Frame();
        Assert.That(output.Input.Text, Is.Empty);
    }

    [Test]
    public void TypedText_IsEmpty_WhileTheKeyboardIsOff()
    {
        var output = new TestOutput();
        output.Input.IsKeyboardEnabled = false;

        output.Type("w");
        output.Frame();

        Assert.That(output.Input.Text, Is.Empty);
    }

    [Test]
    public void Gamepads_ReachOnlyTheOutputThatHasTheKeyboard()
    {
        var backend = new FakeGamepadBackend();
        backend.Connected.Add(new FakeGamepad { State = { Buttons = GamepadButton.A } });
        var output = new TestOutput(backend);

        output.Frame();
        Assert.That(output.Input.IsGamepadButtonDown(0, GamepadButton.A), Is.True);

        output.KeyboardFocused = false;
        output.Frame();
        Assert.That(output.Input.IsGamepadButtonDown(0, GamepadButton.A), Is.False);
    }

    [Test]
    public void InputsWithTheSameFields_AreEqual()
    {
        var key = new KeyboardInput { Key = Keys.W, InputType = InputType.Down };
        var click = new MouseInput { Button = MouseButton.Left, InputType = InputType.Down, ClickCount = 2 };

        Assert.That(key.Equals((object)new KeyboardInput { Key = Keys.W, InputType = InputType.Down }), Is.True);
        Assert.That(click.Equals((object)new MouseInput { Button = MouseButton.Left, InputType = InputType.Down, ClickCount = 2 }), Is.True);
        Assert.That(click.Equals((object)new MouseInput { Button = MouseButton.Left, InputType = InputType.Down, ClickCount = 1 }), Is.False);
    }
}
