using Adamantium.ECS.Components;

namespace Adamantium.EngineTests;

/// <summary>A game component <see cref="PushingService"/> acts on: units per second along X.</summary>
public sealed class Pushed : GameComponent
{
    public double Speed { get; set; } = 1;
}
