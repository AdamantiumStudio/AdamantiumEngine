namespace Adamantium.Localization.Generators;

/// <summary>One attribute of a language file's <c>&lt;Language.Format&gt;</c>.</summary>
public sealed class LanguageFormatValue
{
    public LanguageFormatValue(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public string Value { get; }
}
