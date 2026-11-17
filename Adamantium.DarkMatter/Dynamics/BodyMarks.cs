using System;

namespace Adamantium.DarkMatter.Dynamics;

/// <summary>What happened to a body since the last step's contact pass, which clears it; the body's slot is listed for
/// that once.</summary>
[Flags]
internal enum BodyMarks
{
    None = 0,

    /// <summary>The slot is in the world's list of marked bodies; kept while the slot is free, so it is listed once.
    /// </summary>
    Listed = 1,

    /// <summary>Its box left its leaf in the broad phase: it looks for new pairs.</summary>
    Moved = 2,

    /// <summary>Added since the last pass: its contacts are tested once; a placed body does not wake it.</summary>
    New = 4,

    /// <summary>Put somewhere by the caller - added static, kinematic or awake, or given a pose: wakes old sleepers it
    /// touches.</summary>
    Placed = 8
}
