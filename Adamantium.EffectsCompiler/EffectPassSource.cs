using System.Collections.Generic;

namespace Adamantium.EffectsCompiler;

/// <summary>A pass of an effect file: its name - null for none - and the entry points of its stages and hit groups.
/// </summary>
public sealed class EffectPassSource
{
    internal EffectPassSource(string name, SourceLocation location, IReadOnlyList<EffectStageSource> stages,
        IReadOnlyList<EffectHitGroupSource> hitGroups)
    {
        Name = name;
        Location = location;
        Stages = stages;
        HitGroups = hitGroups;
    }

    public string Name { get; }

    public SourceLocation Location { get; }

    public IReadOnlyList<EffectStageSource> Stages { get; }

    public IReadOnlyList<EffectHitGroupSource> HitGroups { get; }
}
