namespace Adamantium.Localization;

/// <summary>The value a phrase's placeholder is filled with, by the placeholder's name: <c>new("count", 3)</c> for
/// <c>{count}</c>.</summary>
public readonly struct PhraseArgument
{
    public PhraseArgument(string name, object value)
    {
        Name = name;
        Value = value;
    }

    /// <summary>The placeholder's name, as the phrase writes it between braces.</summary>
    public string Name { get; }

    public object Value { get; }

    /// <summary>The value of the placeholder <paramref name="name"/> among <paramref name="arguments"/>; null when none
    /// fills it.</summary>
    public static object ValueOf(IReadOnlyList<PhraseArgument> arguments, string name)
    {
        if (arguments == null)
        {
            return null;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].Name == name)
            {
                return arguments[i].Value;
            }
        }

        return null;
    }
}
