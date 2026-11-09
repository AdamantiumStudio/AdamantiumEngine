namespace Adamantium.Pdf.Navigation;

/// <summary>How a destination shows its page (ISO 32000-2, table 149).</summary>
public enum PdfDestinationKind
{
    /// <summary>A point at its top left, at a zoom (<c>/XYZ</c>).</summary>
    Xyz,

    /// <summary>The whole page in the window (<c>/Fit</c>).</summary>
    Fit,

    /// <summary>The page's width, from a height (<c>/FitH</c>).</summary>
    FitWidth,

    /// <summary>The page's height, from a left edge (<c>/FitV</c>).</summary>
    FitHeight,

    /// <summary>A rectangle in the window (<c>/FitR</c>).</summary>
    FitRectangle,

    /// <summary>The page's content in the window (<c>/FitB</c>).</summary>
    FitContent,

    /// <summary>The content's width, from a height (<c>/FitBH</c>).</summary>
    FitContentWidth,

    /// <summary>The content's height, from a left edge (<c>/FitBV</c>).</summary>
    FitContentHeight,
}
