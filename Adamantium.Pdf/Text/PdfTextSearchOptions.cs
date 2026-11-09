namespace Adamantium.Pdf.Text;

/// <summary>How <see cref="PdfPageText.Find"/> compares text.</summary>
public sealed class PdfTextSearchOptions
{
    public static PdfTextSearchOptions Default { get; } = new();

    /// <summary>Whether upper and lower case differ.</summary>
    public bool MatchCase { get; init; }

    /// <summary>Whether only whole words match.</summary>
    public bool WholeWord { get; init; }

    /// <summary>Whether accents count: "e" finds "é" unless set.</summary>
    public bool MatchDiacritics { get; init; }
}
