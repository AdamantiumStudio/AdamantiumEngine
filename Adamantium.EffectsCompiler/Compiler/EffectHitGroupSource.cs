namespace Adamantium.EffectsCompiler;

/// <summary>A hit group of a ray-tracing pass: <c>HitGroup Name { ClosestHit = ...; AnyHit = ...; Intersection = ...; }
/// </c>, each entry point null when the group has none.</summary>
internal sealed class EffectHitGroupSource(
    string name,
    string closestHit,
    string anyHit,
    string intersection,
    SourceLocation location)
{
    public string Name { get; } = name;

    public string ClosestHit { get; } = closestHit;

    public string AnyHit { get; } = anyHit;

    public string Intersection { get; } = intersection;

    public SourceLocation Location { get; } = location;
}
