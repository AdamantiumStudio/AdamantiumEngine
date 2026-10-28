using System;

namespace Adamantium.ECS;

/// <summary>How long a change goes on showing: for a number of frames, or for a while - a camera's flight, an effect
/// playing out. One frame unless said otherwise.</summary>
public sealed class UniverseChangedEventArgs : EventArgs
{
    /// <summary>A change one frame shows.</summary>
    public static readonly UniverseChangedEventArgs OneFrame = new(1);

    /// <summary>A change the next <paramref name="frames"/> frames show.</summary>
    public UniverseChangedEventArgs(int frames)
    {
        Frames = frames;
    }

    /// <summary>A change that goes on showing for <paramref name="duration"/>.</summary>
    public UniverseChangedEventArgs(TimeSpan duration)
    {
        Duration = duration;
    }

    /// <summary>How many frames show the change.</summary>
    public int Frames { get; }

    /// <summary>How long the change goes on showing.</summary>
    public TimeSpan Duration { get; }
}
