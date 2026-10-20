namespace Adamantium.Multiverse;

/// <summary>How a universe running its own loop paces its frames.</summary>
public enum FrameMode
{
    /// <summary>A frame for every frame its host shows.</summary>
    Continuous,

    /// <summary>Frames only when something asks: input on an output, a resize, a change in the world, or
    /// <see cref="IUniverse.Invalidate()"/>; a still scene draws nothing.</summary>
    OnDemand
}
