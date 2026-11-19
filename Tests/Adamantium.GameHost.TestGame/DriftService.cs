using Adamantium.Core;
using Adamantium.ECS;

namespace Adamantium.GameHost.TestGame;

/// <summary>The game's logic: every entity named Mover drifts along X, one unit a second.</summary>
public sealed class DriftService : GameService
{
    public const string MoverName = "Mover";

    public DriftService(EntityWorld world) : base(world)
    {
    }

    public override void Update(AppTime appTime)
    {
        foreach (var entity in Scene.RootEntities)
        {
            if (entity.Name == MoverName)
                entity.Transform.Position += new Adamantium.Mathematics.Vector3<double>(appTime.FrameTime, 0, 0);
        }
    }
}
