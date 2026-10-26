using Adamantium.Core;
using Adamantium.EffectsCompiler.Compiler;

namespace Adamantium.EffectsCompiler;

/// <summary>A message of an effect's compile and where in the file it stands.</summary>
internal class EffectCompilerMessage(LogMessageType type, string text, SourceLocation location) : LogMessage(type, text)
{
    /// <summary>Where in the file the message stands.</summary>
    public SourceLocation Location { get; } = location;

    public override string ToString() => $"{Location}: {Type.ToString().ToLowerInvariant()}: {Text}";
}
