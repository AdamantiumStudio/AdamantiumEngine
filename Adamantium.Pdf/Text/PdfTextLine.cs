namespace Adamantium.Pdf.Text;

/// <summary>A line of a page's text: a run of its glyphs that follow one another along one baseline.</summary>
public sealed class PdfTextLine
{
    internal PdfTextLine(int start, int count, PdfQuad quad)
    {
        Start = start;
        Count = count;
        Quad = quad;
    }

    /// <summary>The index of its first glyph in <see cref="PdfPageText.Glyphs"/>.</summary>
    public int Start { get; }

    public int Count { get; }

    /// <summary>Its box, from its first glyph's start to its last glyph's end.</summary>
    public PdfQuad Quad { get; }
}
