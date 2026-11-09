namespace Adamantium.Pdf.Measurement;

/// <summary>How the last unit of a measurement writes its value (ISO 32000-2, table 268, <c>F</c>).</summary>
public enum PdfNumberStyle
{
    /// <summary>Decimals, as many as the precision says.</summary>
    Decimal,

    /// <summary>A whole number and a fraction.</summary>
    Fraction,

    /// <summary>Rounded to a whole number.</summary>
    Round,

    /// <summary>Truncated to a whole number.</summary>
    Truncate,
}
