namespace Adamantium.Localization;

/// <summary>One <c>&lt;Phrase Key="..."&gt;</c> of a language file: one text, or a text per case, chosen by the value of
/// a placeholder - a number choosing a plural form (<c>Count</c>), or any value choosing the case named after it
/// (<c>Select</c>).</summary>
public sealed class LanguageEntry
{
    public LanguageEntry(string key, string value, int line, int column)
        : this(key, value, null, false, [], line, column)
    {
    }

    public LanguageEntry(string key, string value, string chooser, bool byNumber,
        IReadOnlyList<LanguageCase> cases, int line, int column)
    {
        Key = key;
        Value = value;
        Chooser = chooser;
        ByNumber = byNumber;
        Cases = cases;
        Line = line;
        Column = column;
    }

    public string Key { get; }

    /// <summary>The text; for a phrase with cases, its Other case, which is what it says in general.</summary>
    public string Value { get; }

    /// <summary>The placeholder whose value chooses the text; null for a phrase with one text.</summary>
    public string Chooser { get; }

    /// <summary>True when a number chooses a plural form, false when a value chooses the case named after it.</summary>
    public bool ByNumber { get; }

    /// <summary>The text of each case the phrase gives; empty for a phrase with one text.</summary>
    public IReadOnlyList<LanguageCase> Cases { get; }

    public bool HasCases => Chooser != null;

    /// <summary>Every text the phrase gives: the one, or each case's.</summary>
    public IEnumerable<string> Texts => HasCases ? Cases.Select(c => c.Text) : [Value];

    public int Line { get; }

    public int Column { get; }
}
