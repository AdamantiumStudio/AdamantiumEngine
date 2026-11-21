using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Adamantium.Mathematics;

namespace Adamantium.ECS.Physics;

/// <summary>A game project's physics (<c>.aphysics</c>): its layers after the default one, the pairs of layers that pass
/// through each other - the rest collide - the gravity and the steps a second. The project names its file; the game's
/// <see cref="PhysicsService"/> takes it as the game starts (<see cref="PhysicsService.Project"/>).</summary>
[XmlRoot("ProjectPhysics")]
public sealed class ProjectPhysics
{
    public const string Extension = ".aphysics";

    /// <summary>The version this engine writes; a file of a later one is refused.</summary>
    public const int CurrentVersion = 1;

    private static readonly XmlSerializer Serializer = new(typeof(ProjectPhysics));

    [XmlAttribute]
    public int Version { get; set; } = CurrentVersion;

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
    public int StepRate { get; set; } = 120;

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
    /// that is not one, of a later version, with a gravity not finite or no steps a second.</summary>
    public static ProjectPhysics Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ProjectPhysics physics;
        try
        {
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            physics = (ProjectPhysics)Serializer.Deserialize(reader);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The file is not a project's physics.", exception);
        }

        if (physics.Version > CurrentVersion)
            throw new InvalidDataException($"The project's physics is of version {physics.Version}; this engine reads "
                                           + $"up to {CurrentVersion}.");

        if (physics.FindProblem() is { } problem)
            throw new InvalidDataException(problem);

        return physics;
    }

    /// <summary>What makes the project's physics one a game cannot take; null when there is nothing.</summary>
    internal string FindProblem()
    {
        if (!float.IsFinite(Gravity.X) || !float.IsFinite(Gravity.Y) || !float.IsFinite(Gravity.Z))
            return "A project's gravity is finite.";

        return StepRate <= 0 ? "A project's physics takes at least one step a second." : null;
    }
}
