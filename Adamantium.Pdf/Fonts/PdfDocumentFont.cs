using System.Collections.Generic;

namespace Adamantium.Pdf.Fonts;

/// <summary>A font a document uses, and on which pages.</summary>
public sealed class PdfDocumentFont
{
    internal PdfDocumentFont(PdfFont font, IReadOnlyList<int> pages)
    {
        Font = font;
        Pages = pages;
    }

    public PdfFont Font { get; }

    /// <summary>The indexes of the pages that use it, from the first.</summary>
    public IReadOnlyList<int> Pages { get; }
}
