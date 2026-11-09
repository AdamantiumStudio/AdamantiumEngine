using System.Collections.Generic;

namespace Adamantium.Pdf.Text;

/// <summary>Where a search found its text: the glyphs it covers and their boxes, one a line.</summary>
public sealed class PdfTextMatch
{
    internal PdfTextMatch(int start, int end, IReadOnlyList<PdfQuad> quads)
    {
        Start = start;
        End = end;
        Quads = quads;
    }

    /// <summary>The index of its first glyph.</summary>
    public int Start { get; }

    /// <summary>The index after its last glyph.</summary>
    public int End { get; }

    public IReadOnlyList<PdfQuad> Quads { get; }
}
