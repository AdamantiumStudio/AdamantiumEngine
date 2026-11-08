using System.Globalization;

namespace Adamantium.Pdf.Objects;

/// <summary>An integer.</summary>
public sealed class PdfInteger : PdfObject
{
    public PdfInteger(long value)
    {
        Value = value;
    }

    public long Value { get; }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
