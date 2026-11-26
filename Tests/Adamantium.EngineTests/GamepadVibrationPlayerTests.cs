using System;
using Adamantium.ECS.Components;
using Adamantium.Engine.EntityServices;
using Adamantium.Multiverse.Input;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class GamepadVibrationPlayerTests
{
    private FakeGamepadBackend backend;
    private GamepadHub hub;
    private ManualClock clock;
    private GamepadVibrationPlayer player;

    [SetUp]
    public void Build()
    {
        backend = new FakeGamepadBackend();
        clock = new ManualClock();
        hub = new GamepadHub(backend, null, clock);
        player = new GamepadVibrationPlayer(hub, clock);
    }

    [Test]
    public void AVibration_PlaysOnEveryGamepad_AsItIsSet()
    {
        var first = Connect();
        var second = Connect();
        var vibration = new GamepadVibration
        {
            LowFrequency = 0.6f, HighFrequency = 0.2f, LowSharpness = 0.4f, HighSharpness = 0.9f,
            LeftTrigger = 0.3f, RightTrigger = 0.8f, Duration = 1
        };

        player.Play(vibration);

        foreach (var gamepad in new[] { first, second })
        {
            Assert.Multiple(() =>
            {
                Assert.That(gamepad.LowFrequency, Is.EqualTo(0.6f));
                Assert.That(gamepad.HighFrequency, Is.EqualTo(0.2f));
                Assert.That(gamepad.LowSharpness, Is.EqualTo(0.4f));
                Assert.That(gamepad.HighSharpness, Is.EqualTo(0.9f));
                Assert.That(gamepad.LeftTrigger, Is.EqualTo(0.3f));
                Assert.That(gamepad.RightTrigger, Is.EqualTo(0.8f));
            });
        }
    }

    [Test]
    public void ItStops_WhenItsTimeIsUp_EachMotorAsFineAsItsOwnAgain()
    {
        var gamepad = Connect();
        player.Play(new GamepadVibration { LowSharpness = 0.5f, HighSharpness = 0.5f, LeftTrigger = 1, Duration = 0.4f });

        clock.Advance(TimeSpan.FromSeconds(0.3));
        player.Update();
        Assert.That(player.IsPlaying, Is.True);
        Assert.That(gamepad.LowFrequency, Is.GreaterThan(0));

        clock.Advance(TimeSpan.FromSeconds(0.2));
        player.Update();
        Assert.Multiple(() =>
        {
            Assert.That(player.IsPlaying, Is.False);
            Assert.That(gamepad.LowFrequency, Is.Zero);
            Assert.That(gamepad.HighFrequency, Is.Zero);
            Assert.That(gamepad.LeftTrigger, Is.Zero);
            Assert.That(gamepad.LowSharpness, Is.EqualTo(Gamepad.DeepSharpness));
            Assert.That(gamepad.HighSharpness, Is.EqualTo(Gamepad.FineSharpness));
        });
    }

    [Test]
    public void APlayAgain_StartsItsTimeAnew()
    {
        var gamepad = Connect();
        var vibration = new GamepadVibration { Duration = 0.4f };
        player.Play(vibration);
        clock.Advance(TimeSpan.FromSeconds(0.3));
        player.Play(vibration);

        clock.Advance(TimeSpan.FromSeconds(0.3));
        player.Update();

        Assert.That(player.IsPlaying, Is.True);
        Assert.That(gamepad.LowFrequency, Is.EqualTo(vibration.LowFrequency));
    }

    [TestCase(-1f, 0f)]
    [TestCase(2f, 1f)]
    [TestCase(float.NaN, 0f)]
    public void LevelsOutOfRange_AreBroughtIn(float asked, float played)
    {
        var gamepad = Connect();

        player.Play(new GamepadVibration { LowFrequency = asked, Duration = float.NaN });

        Assert.That(gamepad.LowFrequency, Is.EqualTo(played));
    }

    private FakeGamepad Connect()
    {
        var gamepad = new FakeGamepad();
        backend.Connected.Add(gamepad);
        hub.Update();
        clock.Advance(GamepadHub.GreetingLength);
        hub.Update();
        return gamepad;
    }
}
