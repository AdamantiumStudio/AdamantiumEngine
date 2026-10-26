using System.Collections.Generic;

namespace Adamantium.EffectsCompiler;

/// <summary>A technique of an effect file: its name - null for none - and its passes in order.</summary>
internal sealed class EffectTechniqueSource(string name, SourceLocation location, IReadOnlyList<EffectPassSource> passes)
{
    public string Name { get; } = name;

    public SourceLocation Location { get; } = location;

    public IReadOnlyList<EffectPassSource> Passes { get; } = passes;
}
