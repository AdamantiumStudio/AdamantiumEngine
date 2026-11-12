namespace Adamantium.Content.Pipeline;

/// <summary>One of the values a <see cref="ImportSettingKind.Choice"/> setting takes.</summary>
public sealed class ImportSettingChoice
{
    public ImportSettingChoice(string value, string label)
    {
        Value = value;
        Label = label;
    }

    /// <summary>The value as the parameters hold it.</summary>
    public string Value { get; }

    /// <summary>How it is said to a person, in English.</summary>
    public string Label { get; }
}
