using System;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Multiverse;

namespace Adamantium.Engine.EntityServices;

/// <summary>Plays a <see cref="GamepadVibration"/> on the gamepads, in a world that has it - the editor's; ask it on the
/// universe's thread.</summary>
public class GamepadVibrationService : EntityService
{
    private GamepadVibrationPlayer player;

    public GamepadVibrationService(EntityWorld world)
        : base(world)
    {
    }

    public override bool IsUpdateService => true;
    public override bool IsRenderingService => false;
    public override EntityServiceType ServiceType => EntityServiceType.Update;

    public override void Initialize()
    {
        if (EntityWorld.Satellites.Get<IUniversePlatform>()?.Gamepads is { } gamepads)
        {
            player = new GamepadVibrationPlayer(gamepads);
        }
    }

    /// <summary>Plays <paramref name="vibration"/> from now, in place of one that plays.</summary>
    public void Play(GamepadVibration vibration)
    {
        if (player == null || vibration == null)
        {
            return;
        }

        player.Play(vibration);
        // Frames come while it plays, to stop it on time in a universe drawn only when asked.
        if (player.IsPlaying)
        {
            vibration.Owner?.NotifyUpdated(TimeSpan.FromSeconds(float.IsFinite(vibration.Duration) ? Math.Max(0, vibration.Duration) : 0) +
                TimeSpan.FromMilliseconds(50));
        }
    }

    public override void Update(AppTime appTime)
    {
        player?.Update();
        base.Update(appTime);
    }

    // Its time is counted by frames: where they stop coming - a game at a breakpoint, a world let go of - so does it.
    public override void UpdateAtBreakpoint()
    {
        StopPlaying();
        base.UpdateAtBreakpoint();
    }

    public override void UnloadContent()
    {
        StopPlaying();
        base.UnloadContent();
    }

    private void StopPlaying()
    {
        if (player is { IsPlaying: true })
        {
            player.Stop();
        }
    }
}
