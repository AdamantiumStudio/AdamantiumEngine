using System.Globalization;

namespace Adamantium.Pdf.Objects;

/// <summary>A real number.</summary>
public sealed class PdfReal : PdfObject
{
    public PdfReal(double value)
    {
        Value = value;
    }

    public double Value { get; }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
