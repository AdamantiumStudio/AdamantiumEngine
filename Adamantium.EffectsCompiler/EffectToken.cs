namespace Adamantium.EffectsCompiler;

/// <summary>A token of an effect file: what it is, its text and where it stands.</summary>
internal readonly struct EffectToken(EffectTokenKind kind, string text, int start, SourceLocation location)
{
    public EffectTokenKind Kind { get; } = kind;

    public string Text { get; } = text;

    /// <summary>The offset of its first character in the file.</summary>
    public int Start { get; } = start;

    /// <summary>The offset just past its last character.</summary>
    public int End => Start + Text.Length;

    public SourceLocation Location { get; } = location;

    /// <summary>How an error names the token: its first character, or the end of the file.</summary>
    public string DisplayText =>Kind == EffectTokenKind.End ? "the end of the file" : Text.Substring(0, 1);

    public bool IsWord(string word) => Kind == EffectTokenKind.Word && Text == word;

    public bool IsMark(char mark) => Kind == EffectTokenKind.Mark && Text[0] == mark;
}
