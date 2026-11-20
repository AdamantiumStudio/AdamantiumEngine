using Adamantium.ECS.Components;
using Adamantium.Mathematics;

namespace Adamantium.EngineTests;

public sealed class Patrol : GameComponent
{
    public double Speed { get; set; }

    public string Route { get; set; }

    public Vector3F Goal { get; set; }

    public WorldPosition Post { get; set; }

    public PatrolMode Mode { get; set; }

    public int Reached { get; private set; }
}
