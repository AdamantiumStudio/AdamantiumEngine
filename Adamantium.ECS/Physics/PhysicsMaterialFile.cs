using System;
using System.ComponentModel;
using System.IO;
using System.Xml.Serialization;
using Adamantium.DarkMatter;

namespace Adamantium.ECS.Physics;

/// <summary>A physics material file (<c>.aphysmat</c>): how a body or a collider that names it rubs and bounces, and the
/// rules by which it meets another's - one material, say ice, for every collider made of it.</summary>
[XmlRoot("PhysicsMaterial")]
public sealed class PhysicsMaterialFile
{
    public const string Extension = ".aphysmat";

    /// <summary>The version this engine writes; a file of a later one is refused. Version 1 had one friction, read as
    /// both the static and the dynamic.</summary>
    public const int CurrentVersion = 2;

    private static readonly XmlSerializer Serializer = new(typeof(PhysicsMaterialFile));
    private string _surface;
    private bool _versionRead;
    private float? _firstVersionFriction;

    /// <summary>The version the file was read at - the first for a file that names none; a file is always written at
    /// <see cref="CurrentVersion"/>.</summary>
    [XmlIgnore]
    public int Version { get; set; } = CurrentVersion;

    [XmlAttribute("Version"), EditorBrowsable(EditorBrowsableState.Never)]
    public int WrittenVersion
    {
        get => CurrentVersion;
        set
        {
            Version = value;
            _versionRead = true;
        }
    }

    [XmlAttribute]
    public float StaticFriction { get; set; } = PhysicsMaterial.Default.StaticFriction;

    [XmlAttribute]
    public float DynamicFriction { get; set; } = PhysicsMaterial.Default.DynamicFriction;

    [XmlAttribute]
    public float RollingResistance { get; set; } = PhysicsMaterial.Default.RollingResistance;

    [XmlAttribute]
    public float Restitution { get; set; } = PhysicsMaterial.Default.Restitution;

    [XmlAttribute]
    public CombineRule FrictionCombine { get; set; } = PhysicsMaterial.Default.FrictionCombine;

    [XmlAttribute]
    public CombineRule RestitutionCombine { get; set; } = PhysicsMaterial.Default.RestitutionCombine;

    /// <summary>What surface the material is, its spaces at the ends left out; null for none - a blank one too.
    /// </summary>
    [XmlAttribute]
    public string Surface
    {
        get => _surface;
        set => _surface = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // Version 1's one friction: both the static and the dynamic, in a file of that version only. Read, never written.
    [XmlAttribute("Friction"), EditorBrowsable(EditorBrowsableState.Never)]
    public float FirstVersionFriction
    {
        get => _firstVersionFriction ?? DynamicFriction;
        set => _firstVersionFriction = value;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeFirstVersionFriction() => false;

    /// <summary>The material the file holds.</summary>
    public PhysicsMaterial ToMaterial() => new()
    {
        StaticFriction = StaticFriction,
        DynamicFriction = DynamicFriction,
        RollingResistance = RollingResistance,
        Restitution = Restitution,
        FrictionCombine = FrictionCombine,
        RestitutionCombine = RestitutionCombine,
        Surface = Surface
    };

    /// <summary>The file of <paramref name="material"/>.</summary>
    public static PhysicsMaterialFile From(in PhysicsMaterial material) => new()
    {
        StaticFriction = material.StaticFriction,
        DynamicFriction = material.DynamicFriction,
        RollingResistance = material.RollingResistance,
        Restitution = material.Restitution,
        FrictionCombine = material.FrictionCombine,
        RestitutionCombine = material.RestitutionCombine,
        Surface = material.Surface
    };

    /// <summary>The material in the file at <paramref name="path"/>; see <see cref="Read(Stream)"/>.</summary>
    public static PhysicsMaterialFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>The material in <paramref name="stream"/>; throws <see cref="InvalidDataException"/> for a file that is
    /// not one, of a later version, or with a friction, rolling resistance or restitution below zero or not finite.
    /// </summary>
    public static PhysicsMaterialFile Read(Stream stream)
    {
        var file = PhysicsXml.Read<PhysicsMaterialFile>(Serializer, stream, "a physics material");
        if (!file._versionRead)
            file.Version = 1;

        if (file.Version < 2 && file._firstVersionFriction is { } friction)
        {
            file.StaticFriction = friction;
            file.DynamicFriction = friction;
        }

        if (file.Version > CurrentVersion)
            throw new InvalidDataException($"The physics material is of version {file.Version}; this engine reads up to "
                                           + $"{CurrentVersion}.");

        if (file.FindProblem() is { } problem)
            throw new InvalidDataException(problem);

        return file;
    }

    /// <summary>Writes the material to <paramref name="path"/>, whole or not at all; throws
    /// <see cref="InvalidOperationException"/> for one <see cref="Read(Stream)"/> would refuse.</summary>
    public void Write(string path)
    {
        ThrowIfUnreadable();
        PhysicsXml.Write(Serializer, this, path);
    }

    /// <summary>Writes the material to <paramref name="stream"/>; see <see cref="Write(string)"/>.</summary>
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

    private string FindProblem() =>
        IsAmount(StaticFriction) && IsAmount(DynamicFriction) && IsAmount(RollingResistance) && IsAmount(Restitution)
            ? null
            : "A physics material's friction, rolling resistance and restitution are finite, not below zero.";

    private static bool IsAmount(float value) => float.IsFinite(value) && value >= 0;
}
