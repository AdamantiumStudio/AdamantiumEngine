namespace Adamantium.DarkMatter;

/// <summary>The English for a <see cref="PhysicsSettingsProblem"/>, for a log or an exception's message.</summary>
public static class PhysicsSettingsProblems
{
    /// <summary>The problem in an English sentence; null for <see cref="PhysicsSettingsProblem.None"/>.</summary>
    public static string Explained(this PhysicsSettingsProblem problem) => problem switch
    {
        PhysicsSettingsProblem.None => null,
        PhysicsSettingsProblem.NoSteps => "The physics takes at least one step a second.",
        PhysicsSettingsProblem.NoSubsteps => "Each step solves the contacts at least once.",
        PhysicsSettingsProblem.ContactPointsOutOfRange => $"A contact keeps from 1 to {PhysicsSettings.MostContactPointsPerPair} points.",
        PhysicsSettingsProblem.NoStepsPerAdvance => "An advance takes at least one step.",
        PhysicsSettingsProblem.GravityNotFinite => "The gravity is finite.",
        PhysicsSettingsProblem.SpeedLimitOutOfRange => "The speed limit is above zero and not too large.",
        PhysicsSettingsProblem.ContactOrSleepOutOfRange => "Contact and sleep settings are finite and not negative.",
        PhysicsSettingsProblem.TimeToSleepOutOfRange => "The time to sleep is a number, not negative.",
        _ => problem.ToString()
    };
}
