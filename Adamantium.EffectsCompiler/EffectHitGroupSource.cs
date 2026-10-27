using System.Collections.Generic;

namespace Adamantium.EffectsCompiler;

/// <summary>A hit group of a ray-tracing pass: <c>HitGroup Name { ClosestHit = ...; AnyHit = ...; Intersection = ...; }
/// </c> - the shaders it names, each once.</summary>
public sealed class EffectHitGroupSource
{
    internal EffectHitGroupSource(string name, SourceLocation location, IReadOnlyList<EffectStageSource> stages)
    {
        Name = name;
        Location = location;
        Stages = stages;
    }

    public string Name { get; }

    public SourceLocation Location { get; }

    public IReadOnlyList<EffectStageSource> Stages { get; }
}
