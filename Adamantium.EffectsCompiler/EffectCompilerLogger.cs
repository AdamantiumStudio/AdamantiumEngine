using Adamantium.Core;

namespace Adamantium.EffectsCompiler;

/// <summary>What compiling an effect has to say: errors, each where in the file it stands. One text said at two places
/// is two messages; at the same place, one.</summary>
internal class EffectCompilerLogger : Logger
{
    /// <summary>Logs an error at <paramref name="location"/>.</summary>
    public void Error(string text, SourceLocation location) =>
        LogMessage(new EffectCompilerMessage(LogMessageType.Error, text, location));

    public override void LogMessage(LogMessage message)
    {
        if (message is not EffectCompilerMessage placed)
        {
            base.LogMessage(message);
            return;
        }

        foreach (var logged in Messages)
        {
            if (logged is EffectCompilerMessage other && other.Text == placed.Text && other.Location.Equals(placed.Location))
            {
                return;
            }
        }

        if (placed.Type == LogMessageType.Error)
        {
            HasErrors = true;
        }

        Messages.Add(placed);
    }
}
