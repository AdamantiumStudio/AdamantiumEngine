using System;
using Adamantium.Core;
using Adamantium.ECS;

namespace Adamantium.EngineTests;

/// <summary>A service of the engine's own kind - not the game's - that fails on every frame.</summary>
public sealed class FailingEngineService : EntityService
{
    public FailingEngineService(EntityWorld world) : base(world)
    {
    }

    public override bool IsUpdateService => true;

    public override bool IsRenderingService => false;

    public override EntityServiceType ServiceType => EntityServiceType.Update;

    public override void Update(AppTime appTime) => throw new InvalidOperationException("The engine's own mistake");
}
