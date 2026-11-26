namespace Adamantium.ECS.Physics;

/// <summary>Why a physics file - a project's physics, a physics material - is not read or not written: a value, so
/// whoever shows it says it in their own words.</summary>
public enum PhysicsFileProblem
{
    /// <summary>Nothing: the file is read.</summary>
    None,

    /// <summary>The file is not a file of its kind: damaged XML, or another file's.</summary>
    NotThisKind,

    /// <summary>The file is of its kind, but a value in it cannot be read - a number that is no number, a choice there
    /// is none of.</summary>
    ValueNotRead,

    /// <summary>The file is of a later version than this engine reads; <see cref="PhysicsFileException.Version"/> says
    /// which, <see cref="PhysicsFileException.ReadsUpTo"/> up to which it reads.</summary>
    LaterVersion,

    /// <summary>The project's physics holds settings no simulation takes; <see cref="PhysicsFileException.Settings"/>
    /// says which.</summary>
    SettingsRefused,

    /// <summary>The project's time to sleep is not finite - bodies that never sleep have sleeping turned off.</summary>
    TimeToSleepNotFinite,

    /// <summary>A material's friction, rolling resistance or restitution is not finite, or below zero.</summary>
    MaterialAmountRefused
}
