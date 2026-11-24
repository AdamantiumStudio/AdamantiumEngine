using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Xml.Serialization;
using Adamantium.DarkMatter;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Physics;

/// <summary>A game project's physics (<c>.aphysics</c>): its layers after the default one, the pairs of layers that pass
/// through each other - the rest collide - the gravity and how the solver steps. The project names its file; the game's
/// <see cref="PhysicsService"/> takes it as the game starts (<see cref="PhysicsService.Project"/>).</summary>
[XmlRoot("ProjectPhysics")]
public sealed class ProjectPhysics
{
    public const string Extension = ".aphysics";

    /// <summary>The version this engine writes; a file of a later one is refused.</summary>
    public const int CurrentVersion = 1;

    private static readonly XmlSerializer Serializer = new(typeof(ProjectPhysics));
    // The solver's own defaults, so a file that leaves a setting out steps as a simulation made with none does.
    private static readonly PhysicsSettings Defaults = new();

    /// <summary>The version the file was read at; a file is always written at <see cref="CurrentVersion"/>.</summary>
    [XmlIgnore]
    public int Version { get; set; } = CurrentVersion;

    [XmlAttribute("Version"), EditorBrowsable(EditorBrowsableState.Never)]
    public int WrittenVersion
    {
        get => CurrentVersion;
        set => Version = value;
    }

    /// <summary>The pull every dynamic body feels, in meters per second squared; down, along +Y, by default.</summary>
    [XmlIgnore]
    public Vector3F Gravity { get; set; } = new(0, 9.81f, 0);

    [XmlAttribute("Gravity"), EditorBrowsable(EditorBrowsableState.Never)]
    public string GravityText
    {
        get => string.Create(CultureInfo.InvariantCulture, $"{Gravity.X}, {Gravity.Y}, {Gravity.Z}");
        set => Gravity = Vector3F.Parse(value);
    }

    /// <summary>How many steps make a second.</summary>
    [XmlAttribute]
    public int StepRate { get; set; } = Defaults.StepRate;

    /// <summary>See <see cref="PhysicsSettings.Substeps"/>.</summary>
    [XmlAttribute]
    public int Substeps { get; set; } = Defaults.Substeps;

    /// <summary>See <see cref="PhysicsSettings.MaxContactPointsPerPair"/>.</summary>
    [XmlAttribute]
    public int MaxContactPointsPerPair { get; set; } = Defaults.MaxContactPointsPerPair;

    /// <summary>See <see cref="PhysicsSettings.SpeculativeDistance"/>.</summary>
    [XmlAttribute]
    public float SpeculativeDistance { get; set; } = Defaults.SpeculativeDistance;

    /// <summary>See <see cref="PhysicsSettings.ContactHertz"/>.</summary>
    [XmlAttribute]
    public float ContactHertz { get; set; } = Defaults.ContactHertz;

    /// <summary>See <see cref="PhysicsSettings.ContactDampingRatio"/>.</summary>
    [XmlAttribute]
    public float ContactDampingRatio { get; set; } = Defaults.ContactDampingRatio;

    /// <summary>See <see cref="PhysicsSettings.MaxPushOutSpeed"/>.</summary>
    [XmlAttribute]
    public float MaxPushOutSpeed { get; set; } = Defaults.MaxPushOutSpeed;

    /// <summary>See <see cref="PhysicsSettings.RestitutionThreshold"/>.</summary>
    [XmlAttribute]
    public float RestitutionThreshold { get; set; } = Defaults.RestitutionThreshold;

    /// <summary>Whether bodies left still fall asleep; when not, every body is stepped every step.</summary>
    [XmlAttribute]
    public bool BodiesSleep { get; set; } = true;

    /// <summary>See <see cref="PhysicsSettings.SleepSpeed"/>.</summary>
    [XmlAttribute]
    public float SleepSpeed { get; set; } = Defaults.SleepSpeed;

    /// <summary>See <see cref="PhysicsSettings.TimeToSleep"/>.</summary>
    [XmlAttribute]
    public float TimeToSleep { get; set; } = Defaults.TimeToSleep;

    /// <summary>See <see cref="PhysicsSettings.MaxLinearSpeed"/>.</summary>
    [XmlAttribute]
    public float MaxLinearSpeed { get; set; } = Defaults.MaxLinearSpeed;

    /// <summary>See <see cref="PhysicsSettings.MaxStepsPerAdvance"/>.</summary>
    [XmlAttribute]
    public int MaxStepsPerAdvance { get; set; } = Defaults.MaxStepsPerAdvance;

    /// <summary>The layers after the default one, in order.</summary>
    [XmlElement("Layer")]
    public List<ProjectLayer> Layers { get; set; } = [];

    /// <summary>The pairs of layers whose bodies pass through each other.</summary>
    [XmlElement("Apart")]
    public List<LayerPair> Apart { get; set; } = [];

    /// <summary>The project's physics in the file at <paramref name="path"/>; see <see cref="Read(Stream)"/>.</summary>
    public static ProjectPhysics Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>The project's physics in <paramref name="stream"/>; throws <see cref="InvalidDataException"/> for a file
    /// that is not one, of a later version, with settings <see cref="PhysicsSettings.FindProblem"/> refuses or a time to
    /// sleep not finite (bodies that never sleep are <see cref="BodiesSleep"/> off).</summary>
    public static ProjectPhysics Read(Stream stream)
    {
        var physics = PhysicsXml.Read<ProjectPhysics>(Serializer, stream, "a project's physics");
        if (physics.Version > CurrentVersion)
            throw new InvalidDataException($"The project's physics is of version {physics.Version}; this engine reads "
                                           + $"up to {CurrentVersion}.");

        if (physics.FindProblem() is { } problem)
            throw new InvalidDataException(problem);

        return physics;
    }

    /// <summary>Writes the project's physics to <paramref name="path"/>, whole or not at all; throws
    /// <see cref="InvalidOperationException"/> for physics <see cref="Read(Stream)"/> would refuse.</summary>
    public void Write(string path)
    {
        ThrowIfUnreadable();
        PhysicsXml.Write(Serializer, this, path);
    }

    /// <summary>Writes the project's physics to <paramref name="stream"/>; see <see cref="Write(string)"/>.</summary>
    public void Write(Stream stream)
    {
        ThrowIfUnreadable();
        PhysicsXml.Write(Serializer, this, stream);
    }

    private void ThrowIfUnreadable()
    {
        if (FindProblem() is { } problem)
            throw new InvalidOperationException(problem);
    }

    /// <summary>The settings a game's simulation is made with: these, the gravity among them.</summary>
    public PhysicsSettings ToSettings() => new()
    {
        Gravity = Gravity,
        StepRate = StepRate,
        Substeps = Substeps,
        MaxContactPointsPerPair = MaxContactPointsPerPair,
        SpeculativeDistance = SpeculativeDistance,
        ContactHertz = ContactHertz,
        ContactDampingRatio = ContactDampingRatio,
        MaxPushOutSpeed = MaxPushOutSpeed,
        RestitutionThreshold = RestitutionThreshold,
        SleepSpeed = SleepSpeed,
        TimeToSleep = BodiesSleep ? TimeToSleep : float.PositiveInfinity,
        MaxLinearSpeed = MaxLinearSpeed,
        MaxStepsPerAdvance = MaxStepsPerAdvance
    };

    /// <summary>What makes the project's physics one a game cannot take; null when there is nothing.</summary>
    internal string FindProblem() => float.IsFinite(TimeToSleep) ? ToSettings().FindProblem() : "The time to sleep is finite.";
}
