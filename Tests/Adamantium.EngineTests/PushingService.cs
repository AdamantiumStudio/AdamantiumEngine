using System.Collections.Generic;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.EngineTests;

/// <summary>A game service of the tests' code: moves every <see cref="Pushed"/> of the scene played along X by its speed,
/// says when it was started and stopped; an edit of a <see cref="Pushed"/> lifts its owner to its speed along Y.</summary>
public sealed class PushingService : GameService
{
    public PushingService(EntityWorld world) : base(world)
    {
    }

    public List<GameComponent> EditsHeard { get; } = [];

    protected override void OnComponentEdited(GameComponent component)
    {
        EditsHeard.Add(component);
        if (component is not Pushed pushed)
            return;

        var position = pushed.Owner.Transform.Position;
        pushed.Owner.Transform.Position = new WorldPosition(position.X, pushed.Speed, position.Z);
    }

    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    /// <summary>How many times it was told the game paused and went on, and whether it was awake when told.</summary>
    public int Pauses { get; private set; }

    public int Resumes { get; private set; }

    public bool WasAwakeWhenPaused { get; private set; }

    public bool WasAsleepWhenResumed { get; private set; }

    protected override void OnStart() => Started = true;

    protected override void OnPause()
    {
        Pauses++;
        WasAwakeWhenPaused = IsActive;
    }

    protected override void OnResume()
    {
        Resumes++;
        WasAsleepWhenResumed = !IsActive;
    }

    public override void Update(AppTime appTime)
    {
        foreach (var pushed in Each<Pushed>())
        {
            pushed.Owner.Transform.Position += new Vector3<double>(pushed.Speed * appTime.FrameTime, 0, 0);
        }
    }

    protected override void OnStop() => Stopped = true;
}
