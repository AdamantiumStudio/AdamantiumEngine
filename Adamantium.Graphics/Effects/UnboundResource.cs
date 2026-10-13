using System.Collections.Generic;
using Serilog;

namespace Adamantium.Graphics.Effects;

/// <summary>Says ONCE per effect+parameter that a pass was applied with a resource it declares left unbound. Once,
/// because this sits on the per-draw path; the useful fact - WHICH parameter nobody set - is the same every frame.</summary>
internal static class UnboundResource
{
    private static readonly HashSet<string> Reported = new();

    public static void ReportOnce(string effect, string parameter)
    {
        var key = $"{effect}.{parameter}";
        lock (Reported)
        {
            if (!Reported.Add(key)) return;
        }

        Log.Logger.Warning(
            "{Key} is not bound to a live resource: the pass reads the stand-in descriptor instead. " +
            "Bind it before applying the pass, or stop declaring it.", key);
    }
}
