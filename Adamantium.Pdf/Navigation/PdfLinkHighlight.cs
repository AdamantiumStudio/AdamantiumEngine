namespace Adamantium.Pdf.Navigation;

/// <summary>How a link answers being pressed (ISO 32000-2, 12.5.6.5, table 176, <c>H</c>).</summary>
public enum PdfLinkHighlight
{
    /// <summary>Its area inverted - the default.</summary>
    Invert,

    None,

    /// <summary>Its border inverted.</summary>
    Outline,

    /// <summary>Its area shown as if pushed below the page.</summary>
    Push,
}
