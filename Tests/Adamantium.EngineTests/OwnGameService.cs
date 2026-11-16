using Adamantium.ECS;

namespace Adamantium.EngineTests;

/// <summary>A game service of the tests' code written without <see cref="GameService"/>, on the interface alone: the
/// engine plays it as any other.</summary>
public sealed class OwnGameService : EntityService, IGameService
{
    public OwnGameService(EntityWorld world) : base(world)
    {
        IsActive = false;
    }

    public Scene Scene { get; private set; }

    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public override bool IsUpdateService => true;

    public override bool IsRenderingService => false;

    public override EntityServiceType ServiceType => EntityServiceType.Update;

    public void Start(Scene scene)
    {
        Scene = scene;
        Starts++;
    }

    public void Stop()
    {
        Scene = null;
        Stops++;
    }
}
