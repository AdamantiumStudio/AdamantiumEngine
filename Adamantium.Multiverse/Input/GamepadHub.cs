using Adamantium.Core.Events;
using Adamantium.Mathematics;
using Adamantium.Multiverse.Events;
using Adamantium.Multiverse.Payloads;
using Serilog;

namespace Adamantium.Multiverse.Input;

/// <summary>
/// The gamepads, polled once per frame. A gamepad belongs to the machine, not to an output, so there is one of these
/// per platform and each output's input reads from it. A gamepad keeps its slot while it is connected; the slot of one
/// that left goes to the next to arrive.
/// </summary>
public class GamepadHub
{
    public const int SlotCount = 8;

    private const float ThumbDeadZone = 0.2f;

    /// <summary>How strongly a gamepad taken in says so: an Elite Series 2 barely makes out half strength.</summary>
    public const float GreetingStrength = 0.7f;

    /// <summary>How fine it buzzes then: a deep rumble is barely felt on an Elite Series 2.</summary>
    public const float GreetingSharpness = Gamepad.FineSharpness;

    /// <summary>How long it says so.</summary>
    public static readonly TimeSpan GreetingLength = TimeSpan.FromMilliseconds(500);

    private readonly IGamepadBackend backend;
    private readonly IEventAggregator events;
    private readonly TimeProvider clock;
    private readonly Gamepad[] slots = new Gamepad[SlotCount];
    private readonly GamepadState[] states = new GamepadState[SlotCount];
    private readonly GamepadButton[] pressed = new GamepadButton[SlotCount];
    private readonly GamepadButton[] released = new GamepadButton[SlotCount];
    private readonly long[] greetedAt = new long[SlotCount];

    public GamepadHub(IGamepadBackend backend, IEventAggregator events, TimeProvider clock = null)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        this.events = events;
        this.clock = clock ?? TimeProvider.System;
    }

    /// <summary>The gamepad in <paramref name="slot"/>, or null when the slot is free.</summary>
    public Gamepad GetGamepad(int slot)
    {
        return IsSlot(slot) ? slots[slot] : null;
    }

    public bool IsButtonDown(int slot, GamepadButton button)
    {
        return IsSlot(slot) && Holds(states[slot].Buttons, button);
    }

    public bool IsButtonPressed(int slot, GamepadButton button)
    {
        return IsSlot(slot) && Holds(pressed[slot], button);
    }

    public bool IsButtonReleased(int slot, GamepadButton button)
    {
        return IsSlot(slot) && Holds(released[slot], button);
    }

    public GamepadState GetState(int slot)
    {
        return IsSlot(slot) ? states[slot] : default;
    }

    public void Update()
    {
        Array.Clear(pressed);
        Array.Clear(released);

        backend.Update();
        var connected = backend.Gamepads;
        DropDeparted(connected);
        SeatArrivals(connected);

        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (slots[slot] != null)
            {
                EndGreeting(slot);
                Read(slot);
            }
        }
    }

    // A gamepad taken in buzzes briefly, so whoever holds it knows the engine has it.
    private void Greet(int slot)
    {
        slots[slot].SetSharpness(GreetingSharpness, GreetingSharpness);
        slots[slot].SetVibration(GreetingStrength, GreetingStrength);
        greetedAt[slot] = clock.GetTimestamp();
    }

    private void EndGreeting(int slot)
    {
        if (greetedAt[slot] == 0 || clock.GetElapsedTime(greetedAt[slot]) < GreetingLength)
        {
            return;
        }

        greetedAt[slot] = 0;
        slots[slot].SetVibration(0, 0);
        slots[slot].SetSharpness(Gamepad.DeepSharpness, Gamepad.FineSharpness);
    }

    private void DropDeparted(IReadOnlyList<Gamepad> connected)
    {
        for (var slot = 0; slot < SlotCount; slot++)
        {
            var gamepad = slots[slot];
            if (gamepad == null || Contains(connected, gamepad))
            {
                continue;
            }

            released[slot] = states[slot].Buttons;
            states[slot] = default;
            slots[slot] = null;
            greetedAt[slot] = 0;
            Log.Logger.Information("Gamepad '{Name}' left slot {Slot}", gamepad.Name, slot);
            events?.GetEvent<GamepadDisconnectedEvent>().Publish(new GamepadPayload(slot, gamepad));
        }
    }

    private void SeatArrivals(IReadOnlyList<Gamepad> connected)
    {
        for (var i = 0; i < connected.Count; i++)
        {
            var gamepad = connected[i];
            if (Array.IndexOf(slots, gamepad) >= 0)
            {
                continue;
            }

            var slot = Array.IndexOf(slots, null);
            if (slot < 0)
            {
                return;
            }

            slots[slot] = gamepad;
            Log.Logger.Information("Gamepad '{Name}' took slot {Slot}", gamepad.Name, slot);
            Greet(slot);
            events?.GetEvent<GamepadConnectedEvent>().Publish(new GamepadPayload(slot, gamepad));
        }
    }

    private void Read(int slot)
    {
        var previous = states[slot].Buttons;
        var state = slots[slot].GetState();
        state.IsConnected = true;
        state.LeftThumb = new Vector2F(DeadZone(state.LeftThumb.X), DeadZone(state.LeftThumb.Y));
        state.RightThumb = new Vector2F(DeadZone(state.RightThumb.X), DeadZone(state.RightThumb.Y));
        state.LeftTrigger = Math.Clamp(state.LeftTrigger, 0f, 1f);
        state.RightTrigger = Math.Clamp(state.RightTrigger, 0f, 1f);

        pressed[slot] = state.Buttons & ~previous;
        released[slot] |= previous & ~state.Buttons;
        states[slot] = state;
    }

    private static float DeadZone(float value)
    {
        var magnitude = Math.Abs(value);
        if (magnitude < ThumbDeadZone)
        {
            return 0;
        }

        return Math.Sign(value) * Math.Min(1f, (magnitude - ThumbDeadZone) / (1 - ThumbDeadZone));
    }

    private static bool Contains(IReadOnlyList<Gamepad> gamepads, Gamepad gamepad)
    {
        for (var i = 0; i < gamepads.Count; i++)
        {
            if (ReferenceEquals(gamepads[i], gamepad))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Holds(GamepadButton buttons, GamepadButton button)
    {
        return button != GamepadButton.None && (buttons & button) == button;
    }

    private static bool IsSlot(int slot)
    {
        return slot >= 0 && slot < SlotCount;
    }
}
