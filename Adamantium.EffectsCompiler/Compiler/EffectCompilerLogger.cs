using Adamantium.Core;

namespace Adamantium.EffectsCompiler;

/// <summary>What compiling an effect has to say: errors and warnings, each where in the file it stands when it stands
/// somewhere.</summary>
internal class EffectCompilerLogger : Logger
{
    /// <summary>Logs an error at <paramref name="location"/>.</summary>
    public void Error(string text, SourceLocation location) =>
        LogMessage(new EffectCompilerMessage(LogMessageType.Error, text, location));

    /// <summary>Logs each of <paramref name="texts"/> that says something as an error of no place - Slang's own
    /// messages name theirs.</summary>
    public void Errors(params string[] texts)
    {
        foreach (var text in texts)
        {
            if (!string.IsNullOrEmpty(text))
            {
                Error(text);
            }
        }
    }

    /// <summary>Logs a warning at <paramref name="location"/>.</summary>
    public void Warning(string text, SourceLocation location) =>
        LogMessage(new EffectCompilerMessage(LogMessageType.Warning, text, location));
}
