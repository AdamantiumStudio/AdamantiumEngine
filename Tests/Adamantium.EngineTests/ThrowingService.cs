using System;
using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Components;

namespace Adamantium.EngineTests;

/// <summary>A game service of the tests' code that fails on every frame, and says how many times it was run; told to, it
/// fails to start as well.</summary>
public sealed class ThrowingService : GameService
{
    public ThrowingService(EntityWorld world) : base(world)
    {
    }

    public int Updates { get; private set; }

    public bool FailsToStart { get; set; }

    public bool Stopped { get; private set; }

    public int EditsHeard { get; private set; }

    /// <summary>Whether it fails on an edit too.</summary>
    public bool FailsOnEdit { get; set; }

    protected override void OnComponentEdited(GameComponent component)
    {
        EditsHeard++;
        if (FailsOnEdit)
            throw new InvalidOperationException("A game service's own mistake on an edit");
    }

    protected override void OnStart()
    {
        if (FailsToStart)
            throw new InvalidOperationException("A game service's own mistake at the start");
    }

    protected override void OnStop() => Stopped = true;

    public override void Update(AppTime appTime)
    {
        Updates++;
        throw new InvalidOperationException("A game service's own mistake");
    }
}
