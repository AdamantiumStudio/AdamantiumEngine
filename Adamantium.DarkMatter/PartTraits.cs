namespace Adamantium.DarkMatter;

/// <summary>What a part of a body has of its own besides its shape; the body's for each it has none of.</summary>
internal readonly struct PartTraits
{
    public PhysicsMaterial? Material { get; init; }
    public Layer? Layer { get; init; }

    /// <summary>Whether the part only tells what enters and leaves it: it pushes nothing and weighs nothing.</summary>
    public bool IsTrigger { get; init; }
}
