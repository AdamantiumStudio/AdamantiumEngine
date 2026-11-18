using System.Collections.Generic;
using Adamantium.DarkMatter;

namespace Adamantium.Engine.Physics;

/// <summary>What the physics thread sends after its steps: the bodies that moved, and those it would not take. Sent back
/// once read, to be filled again, so no step allocates.</summary>
internal sealed class PhysicsStepResult
{
    public readonly List<BodyMotion> Motions = [];

    public readonly List<BodyRefusal> Refusals = [];

    /// <summary>When the last step ended, in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    public long SteppedAt;

    /// <summary>How many steps the result covers since the last one.</summary>
    public int Steps;

    public StepStatistics Statistics;
}
