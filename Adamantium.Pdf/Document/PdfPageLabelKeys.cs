namespace Adamantium.Pdf.Document;

/// <summary>The keys of the page label tree and its labels (ISO 32000-2, 7.9.7 and table 161).</summary>
internal static class PdfPageLabelKeys
{
    public const string Nums = "Nums";
    public const string Kids = "Kids";

    /// <summary>The numbering style.</summary>
    public const string S = "S";

    /// <summary>The prefix.</summary>
    public const string P = "P";

    /// <summary>The first number.</summary>
    public const string St = "St";
}
