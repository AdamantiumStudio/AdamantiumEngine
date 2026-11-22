namespace Adamantium.Localization;

/// <summary>A language table read by key: what <c>{Localize}</c>, the designer and tools ask of it. A generated table
/// implements it explicitly, so none of these names is taken from the keys its strings are given.</summary>
public interface ILanguageTable
{
    /// <summary>The languages the table is written in, its base language first.</summary>
    IReadOnlyList<string> Languages { get; }

    /// <summary>The keys of the table's strings.</summary>
    IReadOnlyList<string> Keys { get; }

    /// <summary>The names of the placeholders the string <paramref name="key"/> fills, in the order of the indexes its
    /// text uses; empty for a string without placeholders.</summary>
    IReadOnlyList<string> PlaceholdersOf(string key);

    /// <summary>The placeholder whose value chooses the text of a string written in cases in at least one language - a
    /// number choosing a plural form ("1 file", "5 files"), or a value choosing the case named after it; null for a
    /// string with one text everywhere.</summary>
    string ChooserOf(string key);

    /// <summary>The string <paramref name="key"/> in <paramref name="language"/>, one of <see cref="Languages"/>, as
    /// written: a string with placeholders keeps its indexes. A string written in cases is in the case
    /// <paramref name="choice"/> takes in that language; one with one text ignores it. The base language's where that
    /// language lacks it; null for a key the table does not have.</summary>
    string Find(string key, string language, object choice);
}
