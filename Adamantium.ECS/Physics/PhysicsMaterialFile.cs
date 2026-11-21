using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Adamantium.DarkMatter;

namespace Adamantium.ECS.Physics;

/// <summary>A physics material file (<c>.aphysmat</c>): how a body or a collider that names it rubs and bounces, and the
/// rules by which it meets another's - one material, say ice, for every collider made of it.</summary>
[XmlRoot("PhysicsMaterial")]
public sealed class PhysicsMaterialFile
{
    public const string Extension = ".aphysmat";

    /// <summary>The version this engine writes; a file of a later one is refused.</summary>
    public const int CurrentVersion = 1;

    private static readonly XmlSerializer Serializer = new(typeof(PhysicsMaterialFile));

    [XmlAttribute]
    public int Version { get; set; } = CurrentVersion;

    [XmlAttribute]
    public float Friction { get; set; } = PhysicsMaterial.Default.Friction;

    [XmlAttribute]
    public float Restitution { get; set; } = PhysicsMaterial.Default.Restitution;

    [XmlAttribute]
    public CombineRule FrictionCombine { get; set; } = PhysicsMaterial.Default.FrictionCombine;

    [XmlAttribute]
    public CombineRule RestitutionCombine { get; set; } = PhysicsMaterial.Default.RestitutionCombine;

    /// <summary>The material the file holds.</summary>
    public PhysicsMaterial ToMaterial() => new()
    {
        Friction = Friction,
        Restitution = Restitution,
        FrictionCombine = FrictionCombine,
        RestitutionCombine = RestitutionCombine
    };

    /// <summary>The material in <paramref name="stream"/>; throws <see cref="InvalidDataException"/> for a file that is
    /// not one, of a later version, or with a friction or restitution below zero or not finite.</summary>
    public static PhysicsMaterialFile Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PhysicsMaterialFile file;
        try
        {
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            file = (PhysicsMaterialFile)Serializer.Deserialize(reader);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The file is not a physics material.", exception);
        }

        if (file.Version > CurrentVersion)
            throw new InvalidDataException($"The physics material is of version {file.Version}; this engine reads up to "
                                           + $"{CurrentVersion}.");

        if (!IsAmount(file.Friction) || !IsAmount(file.Restitution))
            throw new InvalidDataException("A physics material's friction and restitution are finite, not below zero.");

        return file;
    }

    private static bool IsAmount(float value) => float.IsFinite(value) && value >= 0;
}
