namespace Adamantium.DarkMatter;

/// <summary>Where a body or a joint of a <see cref="PhysicsSimulation"/> is in its life.</summary>
public enum PhysicsStatus
{
    /// <summary>Asked for; the simulation takes it at the start of the next step.</summary>
    Adding,

    /// <summary>In the simulation, moving with it.</summary>
    Active,

    /// <summary>Not taken: the simulation says why.</summary>
    Refused,

    /// <summary>Taken out; its id names nothing any more.</summary>
    Removed
}
