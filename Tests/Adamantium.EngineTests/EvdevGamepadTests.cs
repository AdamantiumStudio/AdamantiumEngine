using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Input.Evdev;
using NUnit.Framework;

namespace Adamantium.EngineTests;

public class EvdevGamepadTests
{
    [Test]
    public void Events_BecomeTheState_OnlyAtTheReport()
    {
        var gamepad = XboxLike();

        gamepad.Take(EvdevMapping.EventKey, 0x130, 1);
        var before = gamepad.GetState();
        gamepad.Take(EvdevMapping.EventSync, EvdevMapping.SyncReport, 0);

        Assert.Multiple(() =>
        {
            Assert.That(before.Buttons, Is.EqualTo(GamepadButton.None));
            Assert.That(gamepad.GetState().Buttons, Is.EqualTo(GamepadButton.A));
        });
    }

    [Test]
    public void TheSticks_AreUpPositive_AndTheTriggersFromZero()
    {
        var gamepad = XboxLike();

        gamepad.Take(EvdevMapping.EventAbsolute, EvdevMapping.AxisX, 32767);
        gamepad.Take(EvdevMapping.EventAbsolute, EvdevMapping.AxisY, -32768);
        gamepad.Take(EvdevMapping.EventAbsolute, EvdevMapping.AxisRz, 1023);
        gamepad.Take(EvdevMapping.EventSync, EvdevMapping.SyncReport, 0);
        var state = gamepad.GetState();

        Assert.Multiple(() =>
        {
            Assert.That(state.LeftThumb.X, Is.EqualTo(1).Within(0.001));
            Assert.That(state.LeftThumb.Y, Is.EqualTo(1).Within(0.001), "up on the stick is down in evdev");
            Assert.That(state.RightThumb.X, Is.EqualTo(0).Within(0.001), "an untouched stick rests in the middle");
            Assert.That(state.RightTrigger, Is.EqualTo(1));
            Assert.That(state.LeftTrigger, Is.EqualTo(0));
        });
    }

    [Test]
    public void TheHat_IsTheDpad_AndNorthIsY()
    {
        var gamepad = XboxLike();

        gamepad.Take(EvdevMapping.EventAbsolute, EvdevMapping.AxisHatX, -1);
        gamepad.Take(EvdevMapping.EventAbsolute, EvdevMapping.AxisHatY, 1);
        gamepad.Take(EvdevMapping.EventKey, 0x133, 1);
        gamepad.Take(EvdevMapping.EventSync, EvdevMapping.SyncReport, 0);

        Assert.That(gamepad.GetState().Buttons, Is.EqualTo(GamepadButton.DpadLeft | GamepadButton.DpadDown | GamepadButton.Y));
    }

    [Test]
    public void ATriggerWithoutAnAxis_IsAButton()
    {
        var gamepad = new EvdevGamepad("Generic pad", new (int, int)[0x40]);

        gamepad.Take(EvdevMapping.EventKey, EvdevMapping.ButtonTriggerLeft2, 1);
        gamepad.Take(EvdevMapping.EventSync, EvdevMapping.SyncReport, 0);

        Assert.That(gamepad.GetState().LeftTrigger, Is.EqualTo(1));
    }

    [TestCase("Sony Interactive Entertainment DualSense Wireless Controller", GamepadFace.PlayStation)]
    [TestCase("Microsoft X-Box Series S|X Controller", GamepadFace.Xbox)]
    [TestCase("Nintendo Switch Pro Controller", GamepadFace.Nintendo)]
    [TestCase("8BitDo SN30 Pro", GamepadFace.Unknown)]
    public void TheName_SaysTheFace(string name, GamepadFace face) =>
        Assert.That(EvdevMapping.Face(name), Is.EqualTo(face));

    [Test]
    public void TheBackend_ReadsTheDevicesThereAre_WithoutFailing()
    {
        var backend = new EvdevGamepadBackend();

        Assert.DoesNotThrow(backend.Update);
        Assert.That(backend.Gamepads, Is.Not.Null);
    }

    private static EvdevGamepad XboxLike()
    {
        var ranges = new (int, int)[0x40];
        foreach (var axis in new[] { EvdevMapping.AxisX, EvdevMapping.AxisY, EvdevMapping.AxisRx, EvdevMapping.AxisRy })
        {
            ranges[axis] = (-32768, 32767);
        }

        ranges[EvdevMapping.AxisZ] = (0, 1023);
        ranges[EvdevMapping.AxisRz] = (0, 1023);
        ranges[EvdevMapping.AxisHatX] = (-1, 1);
        ranges[EvdevMapping.AxisHatY] = (-1, 1);
        return new EvdevGamepad("Microsoft X-Box Series S|X Controller", ranges);
    }
}
