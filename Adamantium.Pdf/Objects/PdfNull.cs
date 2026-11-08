namespace Adamantium.Pdf.Objects;

/// <summary>The null object.</summary>
public sealed class PdfNull : PdfObject
{
    private PdfNull()
    {
    }

    /// <summary>The one null.</summary>
    public static PdfNull Value { get; } = new();

    public override string ToString() => "null";
}
