using System;
using Adamantium.Core;
using Adamantium.ECS;

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

    public override void Start()
    {
        if (FailsToStart)
            throw new InvalidOperationException("A game service's own mistake at the start");
    }

    public override void Stop() => Stopped = true;

    public override void Update(AppTime appTime)
    {
        Updates++;
        throw new InvalidOperationException("A game service's own mistake");
    }
}
