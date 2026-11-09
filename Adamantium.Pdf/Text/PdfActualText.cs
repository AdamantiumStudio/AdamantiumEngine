namespace Adamantium.Pdf.Text;

/// <summary>A marked span's <c>/ActualText</c> (ISO 32000-2, 14.9.4): the text its glyphs stand for, given to the
/// first of them.</summary>
internal sealed class PdfActualText(string text)
{
    public string Text { get; } = text;

    /// <summary>Whether a glyph has taken the text.</summary>
    public bool Used { get; set; }
}
