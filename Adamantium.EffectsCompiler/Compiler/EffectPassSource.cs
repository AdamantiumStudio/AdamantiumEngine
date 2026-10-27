using System.Collections.Generic;

namespace Adamantium.EffectsCompiler.Compiler;

/// <summary>A pass of an effect file: its name - null for none - and the entry points of its stages and hit groups.
/// </summary>
internal sealed class EffectPassSource(
    string name,
    SourceLocation location,
    IReadOnlyList<EffectStageSource> stages,
    IReadOnlyList<EffectHitGroupSource> hitGroups)
{
    public string Name { get; } = name;

    public SourceLocation Location { get; } = location;

    public IReadOnlyList<EffectStageSource> Stages { get; } = stages;

    public IReadOnlyList<EffectHitGroupSource> HitGroups { get; } = hitGroups;
}
