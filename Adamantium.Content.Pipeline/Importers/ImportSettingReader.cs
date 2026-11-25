using System;

namespace Adamantium.Content.Pipeline.Importers;

/// <summary>Reads the settings importers are given.</summary>
internal static class ImportSettingReader
{
    /// <summary>One of the <paramref name="allowed"/>; the first where it is not given, or is none of them, which is
    /// logged.</summary>
    public static string ReadChoice(string sourcePath, ContentBuildContext context, string name, params string[] allowed)
    {
        if (!context.Parameters.TryGetValue(name, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return allowed[0];
        }

        foreach (var value in allowed)
        {
            if (string.Equals(value, text.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        context.Log($"{sourcePath} : warning ACB011: the {name} '{text}' is none of {string.Join(", ", allowed)}; " +
                    $"{allowed[0]} is taken");
        return allowed[0];
    }
}
