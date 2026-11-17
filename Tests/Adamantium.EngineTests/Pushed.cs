using Adamantium.ECS.Components;

namespace Adamantium.EngineTests;

/// <summary>A game component <see cref="PushingService"/> acts on: units per second along X.</summary>
public sealed class Pushed : GameComponent
{
    private double _speed = 1;

    public double Speed
    {
        get => _speed;
        set => SetProperty(ref _speed, value);
    }
}
