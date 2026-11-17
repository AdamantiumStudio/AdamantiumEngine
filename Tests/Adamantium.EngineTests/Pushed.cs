using Adamantium.ECS.Components;
using Adamantium.MVVM;

namespace Adamantium.EngineTests;

/// <summary>A game component <see cref="PushingService"/> acts on: units per second along X.</summary>
public sealed partial class Pushed : GameComponent
{
    [Bindable] private double _speed = 1;
}
