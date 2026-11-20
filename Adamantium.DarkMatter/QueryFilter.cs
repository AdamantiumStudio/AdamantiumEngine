namespace Adamantium.DarkMatter;

/// <summary>What a query of a <see cref="PhysicsSimulation"/> passes by.</summary>
public readonly struct QueryFilter
{
    /// <summary>A body passed by: the car a wheel is cast from, say; none for none.</summary>
    public BodyId Except { get; init; }
}
