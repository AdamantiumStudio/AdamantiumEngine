namespace Adamantium.EffectsCompiler.Compiler;

/// <summary>Where in an effect file something is: its path, line and column, both from 1.</summary>
internal readonly struct SourceLocation(string filePath, int line, int column)
{
    public string FilePath { get; } = filePath;

    public int Line { get; } = line;

    public int Column { get; } = column;

    public override string ToString() => $"{FilePath}({Line},{Column})";
}
