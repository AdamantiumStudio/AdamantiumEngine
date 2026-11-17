using System.Collections.Generic;
using Adamantium.DarkMatter;

namespace Adamantium.Engine.Physics;

/// <summary>What the physics thread sends after its steps: the bodies that moved. Sent back once read, to be filled
/// again, so no step allocates.</summary>
internal sealed class PhysicsStepResult
{
    public readonly List<BodyMotion> Motions = [];

    /// <summary>When the last step ended, in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    public long SteppedAt;

    public StepStatistics Statistics;
}
