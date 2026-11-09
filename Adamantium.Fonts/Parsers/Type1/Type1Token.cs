namespace Adamantium.Fonts.Parsers.Type1;

internal readonly struct Type1Token
{
    public Type1Token(Type1TokenKind kind, string text, double number = 0)
    {
        Kind = kind;
        Text = text;
        Number = number;
    }

    public Type1TokenKind Kind { get; }

    /// <summary>The name without its slash, the keyword, or the bracket.</summary>
    public string Text { get; }

    public double Number { get; }

    public bool Is(string keyword) => Kind == Type1TokenKind.Keyword && Text == keyword;

    public override string ToString() => $"{Kind} {Text}";
}
