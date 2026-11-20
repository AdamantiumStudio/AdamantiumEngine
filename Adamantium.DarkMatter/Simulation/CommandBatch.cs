using System.Collections.Generic;

namespace Adamantium.DarkMatter.Simulation;

/// <summary>The commands asked of a <see cref="PhysicsSimulation"/> between two Updates, sent to its thread as one, with
/// the caller's count of it. Given back once taken, to be filled again.</summary>
internal sealed class CommandBatch
{
    public readonly List<SimulationCommand> Commands = [];
    public long Number;
}
