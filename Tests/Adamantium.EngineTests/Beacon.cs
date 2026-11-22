using Adamantium.ECS.Components;
using Adamantium.MVVM;

namespace Adamantium.EngineTests;

/// <summary>A component as the engine's own are made, marked addable: a scene keeps it by its type and fields.</summary>
[Addable("Test", Icon = "BeaconIcon")]
public sealed partial class Beacon : Component
{
    [Bindable] private float _range = 1;

    [Bindable] private string _signal;
}
