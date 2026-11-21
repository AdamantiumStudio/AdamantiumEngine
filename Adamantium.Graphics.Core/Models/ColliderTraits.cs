namespace Adamantium.Graphics.Core.Models;

/// <summary>What a collider is besides its shape: whether it only tells what enters it, the project's layer it is on, and
/// the physics material it rubs and bounces by - each null or false for its body's.</summary>
public sealed record ColliderTraits
{
    /// <summary>Whether the collider only tells what enters and leaves it, pushing and weighing nothing.</summary>
    public bool IsTrigger { get; set; }

    /// <summary>The name of the project's layer the collider is on; null for its body's.</summary>
    public string Layer { get; set; }

    /// <summary>The physics material file the collider rubs and bounces by; null for its body's.</summary>
    public string MaterialPath { get; set; }

    /// <summary>Whether the traits are a new collider's own: nothing to keep.</summary>
    public bool HoldsDefaults() => !IsTrigger && string.IsNullOrEmpty(Layer) && string.IsNullOrEmpty(MaterialPath);
}
