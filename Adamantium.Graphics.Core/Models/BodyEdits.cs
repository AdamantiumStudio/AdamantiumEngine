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

    public float Friction { get; set; } = 0.6f;

    public float Restitution { get; set; }

    public float LinearDamping { get; set; }

    public float AngularDamping { get; set; }

    public float GravityScale { get; set; } = 1f;

    public bool CanSleep { get; set; } = true;
}
