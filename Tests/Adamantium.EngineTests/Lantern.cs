using Adamantium.ECS.Components;
using Adamantium.MVVM;

namespace Adamantium.EngineTests;

/// <summary>A component as the engine's own are made, one that can be turned off, marked addable: a scene keeps it by
/// its type and fields, whether it is on among them.</summary>
[Addable("Test")]
public sealed partial class Lantern : ActivatableComponent
{
    [Bindable] private float _glow = 1;
}
