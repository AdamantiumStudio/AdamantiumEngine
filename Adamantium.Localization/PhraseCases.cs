namespace Adamantium.Localization;

/// <summary>Which case of a phrase chosen by a value (<c>Select</c>) a value takes: True or False, the name of an enum's
/// member, a word as it is, None for no value - and Other for a value no case is named after.</summary>
public static class PhraseCases
{
    /// <summary>The case of every value no other case names.</summary>
    public const string Other = "Other";

    /// <summary>The case of no value at all: "No city chosen" beside "City: {city}". Other where a phrase has none.</summary>
    public const string None = "None";

    public static string Of(object value) => value switch
    {
        null => None,
        bool flag => flag ? "True" : "False",
        Enum member => member.ToString(),
        string { Length: > 0 } word => word,
        _ => Other,
    };
}
