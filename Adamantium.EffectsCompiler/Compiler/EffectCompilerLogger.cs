using Adamantium.Core;

namespace Adamantium.EffectsCompiler.Compiler;

/// <summary>What compiling an effect has to say: errors, each where in the file it stands.</summary>
internal class EffectCompilerLogger : Logger
{
    /// <summary>Logs an error at <paramref name="location"/>.</summary>
    public void Error(string text, SourceLocation location) =>
        LogMessage(new EffectCompilerMessage(LogMessageType.Error, text, location));
}
