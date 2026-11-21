using Adamantium.DarkMatter;

namespace Adamantium.Graphics.Core.Models;

/// <summary>A part of a model as a rigid body: what its rigid body is given when the model is built.</summary>
public sealed class BodyEdits
{
    public BodyKind Kind { get; set; } = BodyKind.Dynamic;

    /// <summary>The mass in kilograms; zero to have it follow from <see cref="Density"/>.</summary>
    public float Mass { get; set; }

    /// <summary>The density, in kilograms per cubic meter.</summary>
    public float Density { get; set; } = 1000f;

    /// <summary>The physics material file the body rubs and bounces by; null for the default material.</summary>
    public string MaterialPath { get; set; }

    /// <summary>The name of the project's layer the body is on; null for the default layer.</summary>
    public string Layer { get; set; }

    public float LinearDamping { get; set; }

    public float AngularDamping { get; set; }

    public float GravityScale { get; set; } = 1f;

    public bool CanSleep { get; set; } = true;
}
