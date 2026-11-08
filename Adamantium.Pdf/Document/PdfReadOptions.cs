using Adamantium.Pdf.Filters;

namespace Adamantium.Pdf.Document;

/// <summary>How a file is opened.</summary>
public sealed class PdfReadOptions
{
    /// <summary>The password to try first; the empty one is tried when it is null or fails.</summary>
    public string Password { get; init; }

    public PdfDecodeLimits DecodeLimits { get; init; } = PdfDecodeLimits.Default;

    public static PdfReadOptions Default { get; } = new();
}
