using Adamantium.ECS.Components;

namespace Adamantium.EngineTests;

public sealed class Patrol : Behavior
{
    public double Speed { get; set; }

    public string Route { get; set; }

    public Vector3F Goal { get; set; }

    public PatrolMode Mode { get; set; }

    public int Reached { get; private set; }
}
