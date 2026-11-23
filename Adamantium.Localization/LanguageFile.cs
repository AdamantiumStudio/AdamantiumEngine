namespace Adamantium.Localization;

/// <summary>One parsed language file: its table and language from the file name, its strings and its formats, and what
/// is wrong with it. The build and the language server read it the same way.</summary>
public sealed class LanguageFile
{
    public LanguageFile(string path, string folder, string table, string language, IReadOnlyList<LanguageEntry> entries,
        IReadOnlyList<LanguageFormatValue> format, int formatLine, IReadOnlyList<LanguageProblem> problems)
    {
        Path = path;
        Folder = folder;
        Table = table;
        Language = language;
        Entries = entries;
        Format = format;
        FormatLine = formatLine;
        Problems = problems;
    }

    public string Path { get; }

    public string Folder { get; }

    public string Table { get; }

    public string Language { get; }

    public IReadOnlyList<LanguageEntry> Entries { get; }

    public IReadOnlyList<LanguageFormatValue> Format { get; }

    public int FormatLine { get; }

    public IReadOnlyList<LanguageProblem> Problems { get; }
}
