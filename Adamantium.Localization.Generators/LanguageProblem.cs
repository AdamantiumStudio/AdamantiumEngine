namespace Adamantium.Localization.Generators;

/// <summary>What is wrong at one place of a language file, under a stable diagnostic id.</summary>
public sealed class LanguageProblem
{
    public LanguageProblem(string id, string message, int line, int column, bool isError = true)
    {
        Id = id;
        Message = message;
        Line = line;
        Column = column;
        IsError = isError;
    }

    public string Id { get; }

    public string Message { get; }

    public int Line { get; }

    public int Column { get; }

    public bool IsError { get; }
}
