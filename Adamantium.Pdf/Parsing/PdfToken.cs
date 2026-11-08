namespace Adamantium.Pdf.Parsing;

/// <summary>One token: where it stands and its value - the number, the string's bytes, the name or keyword.</summary>
internal readonly struct PdfToken
{
    public PdfToken(PdfTokenKind kind, int start, int end, long integer = 0, double real = 0, string text = null,
        byte[] bytes = null)
    {
        Kind = kind;
        Start = start;
        End = end;
        Integer = integer;
        Real = real;
        Text = text;
        Bytes = bytes;
    }

    public PdfTokenKind Kind { get; }

    public int Start { get; }

    public int End { get; }

    public long Integer { get; }

    public double Real { get; }

    /// <summary>A name without its slash, or a keyword.</summary>
    public string Text { get; }

    /// <summary>A string's bytes.</summary>
    public byte[] Bytes { get; }

    public bool IsKeyword(string keyword) => Kind == PdfTokenKind.Keyword && Text == keyword;
}
