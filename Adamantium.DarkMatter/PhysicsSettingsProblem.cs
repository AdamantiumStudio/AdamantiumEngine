namespace Adamantium.DarkMatter;

/// <summary>What makes <see cref="PhysicsSettings"/> ones no simulation takes - a value, so whoever shows it says it in
/// their own words; <see cref="PhysicsSettingsProblems.Explained"/> says it in English for a log or an exception.
/// </summary>
public enum PhysicsSettingsProblem
{
    /// <summary>Nothing: a simulation takes them.</summary>
    None,

    /// <summary>Fewer than one step a second.</summary>
    NoSteps,

    /// <summary>A step that solves the contacts not even once.</summary>
    NoSubsteps,

    /// <summary>A contact keeping fewer than one point, or more than a contact holds.</summary>
    ContactPointsOutOfRange,

    /// <summary>An advance taking not even one step.</summary>
    NoStepsPerAdvance,

    /// <summary>A gravity not finite.</summary>
    GravityNotFinite,

    /// <summary>A speed limit not above zero, or too large to square.</summary>
    SpeedLimitOutOfRange,

    /// <summary>A contact or sleep setting not finite, or below zero.</summary>
    ContactOrSleepOutOfRange,

    /// <summary>A time to sleep below zero, or no number at all.</summary>
    TimeToSleepOutOfRange
}
