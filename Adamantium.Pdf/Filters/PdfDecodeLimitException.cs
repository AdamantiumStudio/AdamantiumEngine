namespace Adamantium.Pdf.Filters;

/// <summary>A stream that would decode past <see cref="PdfDecodeLimits"/>.</summary>
public sealed class PdfDecodeLimitException : PdfException
{
    public PdfDecodeLimitException(long limit) : base("A stream decodes to more than " + limit + " bytes.")
    {
        Limit = limit;
    }

    public long Limit { get; }
}
