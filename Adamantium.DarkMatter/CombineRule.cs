namespace Adamantium.DarkMatter;

/// <summary>How two materials meeting make one friction or one bounce. Two different rules meet by the later one here:
/// <see cref="Maximum"/> over <see cref="Multiply"/> over <see cref="Minimum"/> over <see cref="Average"/>.</summary>
public enum CombineRule
{
    /// <summary>Halfway between the two.</summary>
    Average,

    /// <summary>The lesser of the two.</summary>
    Minimum,

    /// <summary>The two multiplied.</summary>
    Multiply,

    /// <summary>The greater of the two.</summary>
    Maximum
}
