namespace Adamantium.Pdf.Document;

/// <summary>How a page label numbers its pages (ISO 32000-2, 12.4.2, <c>/S</c>).</summary>
public enum PdfPageLabelStyle
{
    /// <summary>No number: the label is the prefix alone.</summary>
    None,

    /// <summary>1, 2, 3.</summary>
    Decimal,

    /// <summary>I, II, III.</summary>
    UpperRoman,

    /// <summary>i, ii, iii.</summary>
    LowerRoman,

    /// <summary>A to Z, then AA to ZZ.</summary>
    UpperLetters,

    /// <summary>a to z, then aa to zz.</summary>
    LowerLetters,
}
