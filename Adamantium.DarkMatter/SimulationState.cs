namespace Adamantium.DarkMatter;

/// <summary>Whether a <see cref="PhysicsSimulation"/> steps on a thread of its own.</summary>
public enum SimulationState
{
    /// <summary>It steps when asked to: <see cref="PhysicsSimulation.Step"/>, <see cref="PhysicsSimulation.Advance"/>.
    /// </summary>
    Stopped,

    /// <summary>It steps on its own thread at its step rate.</summary>
    Running,

    /// <summary>Its thread waits; nothing moves.</summary>
    Paused
}
