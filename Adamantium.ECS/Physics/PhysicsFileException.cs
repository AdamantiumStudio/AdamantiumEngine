using System;
using System.IO;
using Adamantium.DarkMatter;

namespace Adamantium.ECS.Physics;

/// <summary>A physics file that is not read: why, as values - <see cref="Problem"/>, and the version or the
/// <see cref="Settings"/> it names. The message says it in English, for a log. An <see cref="IOException"/>, as what
/// goes wrong reading a file is.</summary>
public sealed class PhysicsFileException : IOException
{
    private PhysicsFileException(string what, PhysicsFileProblem problem, int version, int readsUpTo,
        PhysicsSettingsProblem settings, Exception inner)
        : base(Explain(what, problem, version, readsUpTo, settings), inner)
    {
        Problem = problem;
        Version = version;
        ReadsUpTo = readsUpTo;
        Settings = settings;
    }

    public PhysicsFileProblem Problem { get; }

    /// <summary>The file's version, for <see cref="PhysicsFileProblem.LaterVersion"/>; 0 otherwise.</summary>
    public int Version { get; }

    /// <summary>The latest version this engine reads, for <see cref="PhysicsFileProblem.LaterVersion"/>; 0 otherwise.
    /// </summary>
    public int ReadsUpTo { get; }

    /// <summary>What in the settings no simulation takes, for <see cref="PhysicsFileProblem.SettingsRefused"/>.</summary>
    public PhysicsSettingsProblem Settings { get; }

    /// <summary><paramref name="what"/> is not a file of its kind.</summary>
    internal static PhysicsFileException NotThisKind(string what, Exception inner) =>
        new(what, PhysicsFileProblem.NotThisKind, 0, 0, PhysicsSettingsProblem.None, inner);

    /// <summary>A value in <paramref name="what"/> cannot be read.</summary>
    internal static PhysicsFileException ValueNotRead(string what, Exception inner) =>
        new(what, PhysicsFileProblem.ValueNotRead, 0, 0, PhysicsSettingsProblem.None, inner);

    internal static PhysicsFileException LaterVersion(string what, int version, int readsUpTo) =>
        new(what, PhysicsFileProblem.LaterVersion, version, readsUpTo, PhysicsSettingsProblem.None, null);

    internal static PhysicsFileException SettingsRefused(PhysicsSettingsProblem settings) =>
        new(null, PhysicsFileProblem.SettingsRefused, 0, 0, settings, null);

    internal static PhysicsFileException Refused(PhysicsFileProblem problem) =>
        new(null, problem, 0, 0, PhysicsSettingsProblem.None, null);

    private static string Explain(string what, PhysicsFileProblem problem, int version, int readsUpTo,
        PhysicsSettingsProblem settings) => problem switch
    {
        PhysicsFileProblem.NotThisKind => $"The file is not {what}.",
        PhysicsFileProblem.ValueNotRead => $"A value in {what} cannot be read.",
        PhysicsFileProblem.LaterVersion => $"{what} is of version {version}; this engine reads up to {readsUpTo}.",
        PhysicsFileProblem.SettingsRefused => settings.Explained(),
        PhysicsFileProblem.TimeToSleepNotFinite => "The time to sleep is finite.",
        PhysicsFileProblem.MaterialAmountRefused =>
            "A physics material's friction, rolling resistance and restitution are finite, not below zero.",
        _ => problem.ToString()
    };
}
