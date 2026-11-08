namespace Adamantium.Pdf.Filters;

/// <summary>How far decoding may go before a stream is refused: a few bytes of Flate can claim gigabytes.</summary>
public sealed class PdfDecodeLimits
{
    /// <summary>The largest a decoded stream may be, in bytes: 256 MiB by default, an uncompressed A3 scan at 600 dpi
    /// in color with room to spare.</summary>
    public int MaxDecodedLength { get; init; } = 256 << 20;

    /// <summary>The most filters one stream may chain.</summary>
    public int MaxFilters { get; init; } = 16;

    public static PdfDecodeLimits Default { get; } = new();
}
