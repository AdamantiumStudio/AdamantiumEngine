using Adamantium.Core;
using Adamantium.ECS;
using Adamantium.ECS.Physics;

namespace Adamantium.EngineTests;

/// <summary>A processor attached to the physics beside the main one: it reads where an entity stands each frame.</summary>
internal sealed class PlaceReadingProcessor : EntityProcessor<PhysicsService>
{
    private readonly Entity _entity;

    public PlaceReadingProcessor(Entity entity) => _entity = entity;

    public double LastHeight { get; private set; }

    public override void Update(AppTime appTime) => LastHeight = _entity.Transform.Position.Y;
}
