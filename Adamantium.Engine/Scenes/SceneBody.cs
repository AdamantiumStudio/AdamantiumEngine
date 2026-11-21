using System;
using System.ComponentModel;
using System.Xml.Serialization;
using Adamantium.DarkMatter;
using Adamantium.ECS.Components;
using Adamantium.Graphics.Core.Models;

namespace Adamantium.Engine.Scenes;

/// <summary>The rigid body of a scene's entry: the settings of a <see cref="RigidBody"/>, each written only when it is
/// not a new body's own.</summary>
public sealed class SceneBody
{
    private static readonly RigidBody Defaults = new();

    // A kind this engine does not know, kept as read so the entry is written back as it was.
    private string _unknownKind;

    /// <summary>How it moves; null for a kind this engine does not know, which leaves the body out.</summary>
    [XmlIgnore]
    public BodyKind? Kind { get; set; } = Defaults.Kind;

    [XmlAttribute("Kind"), EditorBrowsable(EditorBrowsableState.Never)]
    public string KindText
    {
        get => Kind?.ToString() ?? _unknownKind;
        set
        {
            Kind = Enum.TryParse<BodyKind>(value, out var kind) && Enum.IsDefined(kind) ? kind : null;
            _unknownKind = Kind == null ? value : null;
        }
    }

    [XmlAttribute]
    public float Mass { get; set; } = Defaults.Mass;

    [XmlAttribute]
    public float Density { get; set; } = Defaults.Density;

    /// <summary>The physics material file the body rubs and bounces by; null for the default material.</summary>
    [XmlAttribute("Material")]
    public string MaterialPath { get; set; }

    /// <summary>The name of the project's layer the body is on; null for the default layer.</summary>
    [XmlAttribute]
    public string Layer { get; set; }

    [XmlAttribute]
    public float LinearDamping { get; set; } = Defaults.LinearDamping;

    [XmlAttribute]
    public float AngularDamping { get; set; } = Defaults.AngularDamping;

    [XmlAttribute]
    public float GravityScale { get; set; } = Defaults.GravityScale;

    [XmlAttribute]
    public bool CanSleep { get; set; } = Defaults.CanSleep;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeKindText() => Kind != Defaults.Kind;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeMass() => Mass != Defaults.Mass;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeDensity() => Density != Defaults.Density;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeMaterialPath() => !string.IsNullOrEmpty(MaterialPath);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeLayer() => !string.IsNullOrEmpty(Layer);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeLinearDamping() => LinearDamping != Defaults.LinearDamping;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeAngularDamping() => AngularDamping != Defaults.AngularDamping;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeGravityScale() => GravityScale != Defaults.GravityScale;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeCanSleep() => CanSleep != Defaults.CanSleep;

    /// <summary>The body's settings, as a model's edits keep them; null for a kind this engine does not know.</summary>
    public BodyEdits ToEdits() => Kind is { } kind
        ? new BodyEdits
        {
            Kind = kind, Mass = Mass, Density = Density, MaterialPath = MaterialPath, Layer = Layer,
            LinearDamping = LinearDamping, AngularDamping = AngularDamping, GravityScale = GravityScale,
            CanSleep = CanSleep
        }
        : null;

    /// <summary>The entry of a body of <paramref name="edits"/>' settings.</summary>
    public static SceneBody From(BodyEdits edits) => new()
    {
        Kind = edits.Kind, Mass = edits.Mass, Density = edits.Density, MaterialPath = edits.MaterialPath,
        Layer = edits.Layer, LinearDamping = edits.LinearDamping, AngularDamping = edits.AngularDamping,
        GravityScale = edits.GravityScale, CanSleep = edits.CanSleep
    };
}
