namespace Adamantium.Localization;

/// <summary>One case of a phrase that changes with a value: the plural form or the value it is said for, and its
/// text.</summary>
public sealed class LanguageCase
{
    public LanguageCase(string name, string text)
    {
        Name = name;
        Text = text;
    }

    /// <summary>The plural form (<c>One</c>, <c>Other</c>...) or the value (<c>True</c>, an enum's member...) the case
    /// is said for.</summary>
    public string Name { get; }

    public string Text { get; }
}
