using System.Collections.Generic;

namespace Adamantium.EffectsCompiler;

/// <summary>A technique of an effect file: its name - null for none - and its passes in order.</summary>
public sealed class EffectTechniqueSource
{
    internal EffectTechniqueSource(string name, SourceLocation location, IReadOnlyList<EffectPassSource> passes)
    {
        Name = name;
        Location = location;
        Passes = passes;
    }

    public string Name { get; }

    public SourceLocation Location { get; }

    public IReadOnlyList<EffectPassSource> Passes { get; }
}
