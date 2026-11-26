using System;
using Adamantium.ECS.Components;
using Adamantium.Multiverse.Input;

namespace Adamantium.Engine.EntityServices;

/// <summary>Plays a <see cref="GamepadVibration"/> on every connected gamepad, and stops it when its time is up: the motors
/// still, their sharpness each motor's own again.</summary>
public sealed class GamepadVibrationPlayer
{
    private readonly GamepadHub gamepads;
    private readonly TimeProvider clock;
    private long startedAt;
    private TimeSpan length;

    public GamepadVibrationPlayer(GamepadHub gamepads, TimeProvider clock = null)
    {
        this.gamepads = gamepads ?? throw new ArgumentNullException(nameof(gamepads));
        this.clock = clock ?? TimeProvider.System;
    }

    public bool IsPlaying { get; private set; }

    /// <summary>Plays <paramref name="vibration"/> from now, in place of one that plays.</summary>
    public void Play(GamepadVibration vibration)
    {
        ArgumentNullException.ThrowIfNull(vibration);
        for (var slot = 0; slot < GamepadHub.SlotCount; slot++)
        {
            var gamepad = gamepads.GetGamepad(slot);
            if (gamepad == null)
            {
                continue;
            }

            gamepad.SetSharpness(Level(vibration.LowSharpness), Level(vibration.HighSharpness));
            gamepad.SetVibration(Level(vibration.LowFrequency), Level(vibration.HighFrequency));
            gamepad.SetTriggerVibration(Level(vibration.LeftTrigger), Level(vibration.RightTrigger));
        }

        length = TimeSpan.FromSeconds(Math.Max(0, float.IsFinite(vibration.Duration) ? vibration.Duration : 0));
        startedAt = clock.GetTimestamp();
        IsPlaying = true;
    }

    /// <summary>Once a frame: stops what played its time.</summary>
    public void Update()
    {
        if (IsPlaying && clock.GetElapsedTime(startedAt) >= length)
        {
            Stop();
        }
    }

    /// <summary>Stops it now.</summary>
    public void Stop()
    {
        IsPlaying = false;
        for (var slot = 0; slot < GamepadHub.SlotCount; slot++)
        {
            var gamepad = gamepads.GetGamepad(slot);
            if (gamepad == null)
            {
                continue;
            }

            gamepad.SetVibration(0, 0);
            gamepad.SetTriggerVibration(0, 0);
            gamepad.SetSharpness(Gamepad.DeepSharpness, Gamepad.FineSharpness);
        }
    }

    private static float Level(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
