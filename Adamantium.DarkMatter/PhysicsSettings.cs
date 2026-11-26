using Adamantium.DarkMatter.CollisionDetection;

namespace Adamantium.DarkMatter;

/// <summary>How a <see cref="PhysicsSimulation"/> steps and solves; read once, when it is made. A copy with some told
/// otherwise is made by <c>with</c>.</summary>
public sealed record PhysicsSettings
{
    /// <summary>The pull every dynamic body feels, in meters per second squared; down along negative Y by default.
    /// The simulation's <see cref="PhysicsSimulation.Gravity"/> starts at it and can change later.</summary>
    public Vector3F Gravity { get; init; } = new(0, -9.81f, 0);

    /// <summary>How many steps make a second; every <see cref="PhysicsSimulation.Step"/> moves the world by one over
    /// this.</summary>
    public int StepRate { get; init; } = 120;

    /// <summary>How many times each step solves the contacts, each over a share of the step; more hold stacks firmer.
    /// </summary>
    public int Substeps { get; init; } = 4;

    /// <summary>The most points a contact between two shapes keeps. Four hold any shape still on a flat face; the
    /// limit is for one pair of shapes, not for a body.</summary>
    public int MaxContactPointsPerPair { get; init; } = 4;

    /// <summary>How close, in meters, two shapes are found touching before they touch, so a fast body stops at the
    /// surface instead of sinking in. The distance a body covers in a step is added to it.</summary>
    public float SpeculativeDistance { get; init; } = 0.02f;

    /// <summary>How stiff contacts are, as the frequency of a spring, in hertz.</summary>
    public float ContactHertz { get; init; } = 30f;

    /// <summary>How quickly a contact's spring settles; above 1 it does not ring.</summary>
    public float ContactDampingRatio { get; init; } = 10f;

    /// <summary>The fastest, in meters per second, any body moves; faster velocities are cut down to it.</summary>
    public float MaxLinearSpeed { get; init; } = 500f;

    /// <summary>The fastest, in meters per second, a contact pushes overlapping bodies apart.</summary>
    public float MaxPushOutSpeed { get; init; } = 3f;

    /// <summary>The least closing speed, in meters per second, a contact bounces at; slower ones only stop.</summary>
    public float RestitutionThreshold { get; init; } = 1f;

    /// <summary>The speed, in meters per second, a body counts as still below, its spin measured at its farthest point.
    /// </summary>
    public float SleepSpeed { get; init; } = 0.05f;

    /// <summary>How long, in seconds, all bodies touching each other stay still and unpushed before they fall asleep
    /// together.</summary>
    public float TimeToSleep { get; init; } = 0.5f;

    /// <summary>The most steps one advance takes when the game falls behind; the time past them is dropped, so the world
    /// slows down instead of stepping ever more to catch up.</summary>
    public int MaxStepsPerAdvance { get; init; } = 8;

    /// <summary>What makes these settings ones no simulation takes; <see cref="PhysicsSettingsProblem.None"/> when there
    /// is nothing.</summary>
    public PhysicsSettingsProblem FindProblem()
    {
        if (StepRate <= 0)
            return PhysicsSettingsProblem.NoSteps;
        if (Substeps <= 0)
            return PhysicsSettingsProblem.NoSubsteps;
        if (MaxContactPointsPerPair <= 0 || MaxContactPointsPerPair > ManifoldBuffer.Capacity)
            return PhysicsSettingsProblem.ContactPointsOutOfRange;
        if (MaxStepsPerAdvance <= 0)
            return PhysicsSettingsProblem.NoStepsPerAdvance;
        if (!float.IsFinite(Gravity.X) || !float.IsFinite(Gravity.Y) || !float.IsFinite(Gravity.Z))
            return PhysicsSettingsProblem.GravityNotFinite;
        if (!(MaxLinearSpeed > 0) || !float.IsFinite(MaxLinearSpeed * MaxLinearSpeed))
            return PhysicsSettingsProblem.SpeedLimitOutOfRange;
        if (!IsFiniteNotNegative(SpeculativeDistance) || !IsFiniteNotNegative(ContactHertz)
            || !IsFiniteNotNegative(ContactDampingRatio) || !IsFiniteNotNegative(MaxPushOutSpeed)
            || !IsFiniteNotNegative(RestitutionThreshold) || !IsFiniteNotNegative(SleepSpeed))
            return PhysicsSettingsProblem.ContactOrSleepOutOfRange;

        // NaN fails the comparison, so it is refused here too.
        return TimeToSleep >= 0 ? PhysicsSettingsProblem.None : PhysicsSettingsProblem.TimeToSleepOutOfRange;
    }

    private static bool IsFiniteNotNegative(float value) => float.IsFinite(value) && value >= 0;
}
