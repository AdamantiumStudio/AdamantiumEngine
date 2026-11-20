namespace Adamantium.DarkMatter.CollisionDetection;

/// <summary>Where a cast shape is on its way to another.</summary>
internal enum SweepPhase
{
    /// <summary>Not yet within the stopping gap.</summary>
    Approaching,

    /// <summary>Out of steps short of the stopping gap: stopped where it stood, the stop moved on once it gets there.
    /// </summary>
    StoppedShort,

    /// <summary>Stopped within the stopping gap, stepping on to tell whether it meets the other.</summary>
    Stopped
}
