using System;

namespace Adamantium.EffectsCompiler;

/// <summary>A technique that breaks the effect language where reading it cannot go on: <see cref="EffectParser"/>
/// reports it and skips the rest of the technique.</summary>
internal sealed class EffectSyntaxException(string message, SourceLocation location) : Exception(message)
{
    public SourceLocation Location { get; } = location;
}
