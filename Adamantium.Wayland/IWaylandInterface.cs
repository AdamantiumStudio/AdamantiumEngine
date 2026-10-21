namespace Adamantium.Wayland;

/// <summary>What every generated interface class provides about itself, so that one can be made by its type: bound
/// from the registry, or created by a request.</summary>
public unsafe interface IWaylandInterface<TSelf> where TSelf : WaylandProxy, IWaylandInterface<TSelf>
{
    static abstract WlInterface* Interface { get; }

    static abstract TSelf Create(nint handle, uint version);
}
