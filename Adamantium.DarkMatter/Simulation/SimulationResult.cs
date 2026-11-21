using System.Collections.Generic;

namespace Adamantium.DarkMatter.Simulation;

/// <summary>What one run of the stepper - the commands handed to it, then its steps - left for the caller: the bodies
/// and joints that may have moved, what it tells of them, and the steps. Given back once read, to be filled again.
/// </summary>
internal sealed class SimulationResult
{
    public readonly List<BodyReading> Bodies = [];
    public readonly List<JointReading> Joints = [];
    public readonly List<SimulationNews> News = [];

    /// <summary>The touches begun and ended in the run, in order.</summary>
    public readonly List<ContactEvent> Contacts = [];

    /// <summary>How many steps the run took, and the simulation's count of steps after them.</summary>
    public int Steps;
    public long StepNumber;
    public StepStatistics Statistics;

    /// <summary>The last batch of commands the run did, by the caller's count of batches handed over.</summary>
    public long CommandBatch;

    /// <summary>When its last step was done, as a <see cref="System.Diagnostics.Stopwatch"/> timestamp.</summary>
    public long SteppedAt;

    public void Clear()
    {
        Bodies.Clear();
        Joints.Clear();
        News.Clear();
        Contacts.Clear();
        Steps = 0;
    }
}
