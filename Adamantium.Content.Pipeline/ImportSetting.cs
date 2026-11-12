using System;
using System.Collections.Generic;

namespace Adamantium.Content.Pipeline;

/// <summary>A setting an importer reads from an asset's parameters: under what name, of what kind, what it is when not
/// given, and how it is said to a person - enough for a tool to offer it without knowing the importer.</summary>
public sealed class ImportSetting
{
    public ImportSetting(string name, ImportSettingKind kind, string defaultValue, string label,
        IReadOnlyList<ImportSettingChoice> choices = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
        Kind = kind;
        DefaultValue = defaultValue ?? string.Empty;
        Label = label ?? name;
        Choices = choices ?? [];
    }

    /// <summary>The parameter's key.</summary>
    public string Name { get; }

    public ImportSettingKind Kind { get; }

    /// <summary>The value the importer takes when the parameters do not give it.</summary>
    public string DefaultValue { get; }

    /// <summary>How the setting is said to a person, in English.</summary>
    public string Label { get; }

    /// <summary>The values a <see cref="ImportSettingKind.Choice"/> setting takes; empty for the other kinds.</summary>
    public IReadOnlyList<ImportSettingChoice> Choices { get; }
}
