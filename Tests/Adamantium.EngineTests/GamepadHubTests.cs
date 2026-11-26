using System.Collections.Generic;
using Adamantium.Core.Events;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Events;
using Adamantium.Multiverse.Input;
using Adamantium.Multiverse.Payloads;
using NUnit.Framework;

namespace Adamantium.EngineTests;

[TestFixture]
public class GamepadHubTests
{
    private FakeGamepadBackend backend;
    private EventAggregator events;
    private GamepadHub hub;
    private ManualClock clock;
    private List<GamepadPayload> connected;
    private List<GamepadPayload> disconnected;

    [SetUp]
    public void Build()
    {
        backend = new FakeGamepadBackend();
        events = new EventAggregator();
        clock = new ManualClock();
        hub = new GamepadHub(backend, events, clock);
        connected = [];
        disconnected = [];
        events.GetEvent<GamepadConnectedEvent>().Subscribe(connected.Add);
        events.GetEvent<GamepadDisconnectedEvent>().Subscribe(disconnected.Add);
    }

    [Test]
    public void AnArrivingGamepad_TakesTheFirstFreeSlot_AndIsAnnounced()
    {
        var gamepad = new FakeGamepad();
        backend.Connected.Add(gamepad);

        hub.Update();

        Assert.That(backend.Updates, Is.EqualTo(1));
        Assert.That(hub.GetGamepad(0), Is.SameAs(gamepad));
        Assert.That(hub.GetState(0).IsConnected, Is.True);
        Assert.That(connected, Has.Count.EqualTo(1));
        Assert.That(connected[0].Slot, Is.EqualTo(0));
        Assert.That(connected[0].Gamepad, Is.SameAs(gamepad));
    }

    [Test]
    public void AButton_IsPressedOnce_HeldWhileDown_AndReleasedOnce()
    {
        var gamepad = new FakeGamepad();
        backend.Connected.Add(gamepad);
        gamepad.State.Buttons = GamepadButton.A;

        hub.Update();
        Assert.That(hub.IsButtonPressed(0, GamepadButton.A), Is.True);
        Assert.That(hub.IsButtonDown(0, GamepadButton.A), Is.True);

        hub.Update();
        Assert.That(hub.IsButtonPressed(0, GamepadButton.A), Is.False);
        Assert.That(hub.IsButtonDown(0, GamepadButton.A), Is.True);

        gamepad.State.Buttons = GamepadButton.None;
        hub.Update();
        Assert.That(hub.IsButtonReleased(0, GamepadButton.A), Is.True);
        Assert.That(hub.IsButtonDown(0, GamepadButton.A), Is.False);

        hub.Update();
        Assert.That(hub.IsButtonReleased(0, GamepadButton.A), Is.False);
    }

    [Test]
    public void AGamepadThatLeaves_ReleasesWhatItHeld_FreesItsSlot_AndIsAnnounced()
    {
        var gamepad = new FakeGamepad();
        backend.Connected.Add(gamepad);
        gamepad.State.Buttons = GamepadButton.Paddle1 | GamepadButton.B;
        hub.Update();

        backend.Connected.Remove(gamepad);
        hub.Update();

        Assert.That(hub.IsButtonReleased(0, GamepadButton.Paddle1), Is.True);
        Assert.That(hub.IsButtonReleased(0, GamepadButton.B), Is.True);
        Assert.That(hub.IsButtonDown(0, GamepadButton.Paddle1), Is.False);
        Assert.That(hub.GetGamepad(0), Is.Null);
        Assert.That(hub.GetState(0).IsConnected, Is.False);
        Assert.That(disconnected, Has.Count.EqualTo(1));
        Assert.That(disconnected[0].Slot, Is.EqualTo(0));
    }

    [Test]
    public void TheOthers_KeepTheirSlots_WhenOneLeaves_AndTheNextArrivalTakesTheFreedOne()
    {
        var first = new FakeGamepad();
        var second = new FakeGamepad();
        backend.Connected.Add(first);
        backend.Connected.Add(second);
        hub.Update();

        backend.Connected.Remove(first);
        var third = new FakeGamepad();
        backend.Connected.Add(third);
        hub.Update();

        Assert.That(hub.GetGamepad(1), Is.SameAs(second));
        Assert.That(hub.GetGamepad(0), Is.SameAs(third));
    }

    [Test]
    public void Sticks_ReadZeroInsideTheDeadZone_AndTheFullRangeOutsideIt()
    {
        var gamepad = new FakeGamepad();
        backend.Connected.Add(gamepad);
        gamepad.State.LeftThumb = new Vector2F(0.1f, 0.6f);
        gamepad.State.RightThumb = new Vector2F(-1f, 1f);
        gamepad.State.LeftTrigger = 1.5f;

        hub.Update();

        var state = hub.GetState(0);
        Assert.That(state.LeftThumb.X, Is.EqualTo(0f));
        Assert.That(state.LeftThumb.Y, Is.EqualTo(0.5f).Within(1e-6f));
        Assert.That(state.RightThumb.X, Is.EqualTo(-1f));
        Assert.That(state.RightThumb.Y, Is.EqualTo(1f));
        Assert.That(state.LeftTrigger, Is.EqualTo(1f));
    }

    [Test]
    public void ASlotOutOfRange_ReadsNothing()
    {
        backend.Connected.Add(new FakeGamepad { State = { Buttons = GamepadButton.A } });
        hub.Update();

        Assert.That(hub.IsButtonDown(-1, GamepadButton.A), Is.False);
        Assert.That(hub.IsButtonDown(GamepadHub.SlotCount, GamepadButton.A), Is.False);
        Assert.That(hub.GetGamepad(GamepadHub.SlotCount), Is.Null);
        Assert.That(hub.GetState(-1).IsConnected, Is.False);
    }

    [Test]
    public void NoButton_IsNeverDown()
    {
        backend.Connected.Add(new FakeGamepad { State = { Buttons = GamepadButton.A } });
        hub.Update();

        Assert.That(hub.IsButtonDown(0, GamepadButton.None), Is.False);
    }

    [Test]
    public void AnArrivingGamepad_BuzzesLightly_ThenStops()
    {
        var gamepad = new FakeGamepad();
        backend.Connected.Add(gamepad);

        hub.Update();
        Assert.That(gamepad.LowFrequency, Is.EqualTo(GamepadHub.GreetingStrength));
        Assert.That(gamepad.HighFrequency, Is.EqualTo(GamepadHub.GreetingStrength));

        clock.Advance(GamepadHub.GreetingLength / 2);
        hub.Update();
        Assert.That(gamepad.VibrationsSet, Is.EqualTo(1), "still buzzing");

        clock.Advance(GamepadHub.GreetingLength);
        hub.Update();
        hub.Update();
        Assert.That(gamepad.LowFrequency, Is.Zero);
        Assert.That(gamepad.HighFrequency, Is.Zero);
        Assert.That(gamepad.VibrationsSet, Is.EqualTo(2), "stopped once");
    }

    [Test]
    public void AGamepadThatStays_IsGreetedOnce()
    {
        var gamepad = new FakeGamepad();
        backend.Connected.Add(gamepad);
        hub.Update();
        clock.Advance(GamepadHub.GreetingLength * 2);
        hub.Update();

        for (var i = 0; i < 10; i++)
        {
            clock.Advance(GamepadHub.GreetingLength);
            hub.Update();
        }

        Assert.That(gamepad.VibrationsSet, Is.EqualTo(2));
    }

    [Test]
    public void AGamepadThatComesBack_IsGreetedAgain()
    {
        var gamepad = new FakeGamepad();
        backend.Connected.Add(gamepad);
        hub.Update();
        backend.Connected.Clear();
        hub.Update();
        clock.Advance(GamepadHub.GreetingLength * 2);
        hub.Update();
        Assert.That(gamepad.VibrationsSet, Is.EqualTo(1), "gone before it was stopped: nothing more is told to it");

        backend.Connected.Add(gamepad);
        hub.Update();

        Assert.That(gamepad.VibrationsSet, Is.EqualTo(2));
        Assert.That(gamepad.LowFrequency, Is.EqualTo(GamepadHub.GreetingStrength));
    }
}
